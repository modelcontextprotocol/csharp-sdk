using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Verifies how request <c>_meta</c> is surfaced on typed request parameters.
/// </summary>
public class RequestParamsMetaTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    [Fact]
    public async Task TypedMeta_MatchesRequestMeta_AndIsIndependentOfIt()
    {
        string json = """
            {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{
              "name":"echo",
              "arguments":{"text":"hi"},
              "_meta":{"progressToken":"token-1","example.com/nested":{"a":[1,2,{"b":null}],"c":"\u00e9"},"example.com/flag":true}
            }}
            """;

        var (response, captured) = await InvokeToolAsync(json, LoggerFactory);

        Assert.IsType<JsonRpcResponse>(response);
        Assert.NotNull(captured);
        CallToolRequestParams typed = captured.Value.Typed;
        JsonObject rawParams = Assert.IsType<JsonObject>(captured.Value.RawParams);
        JsonObject rawMeta = Assert.IsType<JsonObject>(rawParams["_meta"]);

        Assert.Equal("echo", typed.Name);
        Assert.Equal("hi", typed.Arguments!["text"].GetString());
        Assert.NotNull(typed.Meta);
        Assert.True(JsonNode.DeepEquals(rawMeta, typed.Meta));
        Assert.Equal("token-1", typed.ProgressToken?.ToString());

        // The typed metadata is a detached, independent node.
        Assert.NotSame(rawMeta, typed.Meta);
        Assert.Null(typed.Meta.Parent);
        typed.Meta["example.com/added"] = 42;
        ((JsonObject)typed.Meta["example.com/nested"]!)["c"] = "changed";
        Assert.False(rawMeta.ContainsKey("example.com/added"));
        Assert.Equal("\u00e9", rawMeta["example.com/nested"]!["c"]!.GetValue<string>());

        // It can be attached to another node without conflicting with the original request.
        var outgoing = new JsonObject { ["_meta"] = typed.Meta };
        Assert.Same(outgoing, typed.Meta.Parent);
    }

    [Fact]
    public async Task TypedMeta_IsNull_WhenRequestHasNoMeta()
    {
        string json = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"echo","arguments":{"text":"hi"}}}""";

        var (response, captured) = await InvokeToolAsync(json, LoggerFactory);

        Assert.IsType<JsonRpcResponse>(response);
        Assert.NotNull(captured);
        Assert.Equal("echo", captured.Value.Typed.Name);
        Assert.Null(captured.Value.Typed.Meta);
    }

    [Fact]
    public async Task TypedMeta_IsNull_WhenRequestMetaIsNull()
    {
        string json = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"echo","_meta":null}}""";

        var (response, captured) = await InvokeToolAsync(json, LoggerFactory);

        Assert.IsType<JsonRpcResponse>(response);
        Assert.NotNull(captured);
        Assert.Null(captured.Value.Typed.Meta);
    }

    [Theory]
    [InlineData("\"not-an-object\"")]
    [InlineData("[1,2]")]
    [InlineData("42")]
    public async Task NonObjectMeta_IsRejected(string metaJson)
    {
        string json = $$$"""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"echo","_meta":{{{metaJson}}}}}""";

        var (response, captured) = await InvokeToolAsync(json, LoggerFactory);

        Assert.Null(captured);
        Assert.IsType<JsonRpcError>(response);
    }

    [Fact]
    public async Task InvalidNonMetaParams_AreStillRejected_WhenMetaIsAnObject()
    {
        string json = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":42,"_meta":{"a":1}}}""";

        var (response, captured) = await InvokeToolAsync(json, LoggerFactory);

        Assert.Null(captured);
        Assert.IsType<JsonRpcError>(response);
    }

    internal static async Task<(JsonRpcMessage Response, (CallToolRequestParams Typed, JsonNode? RawParams)? Captured)> InvokeToolAsync(
        string requestJson, Microsoft.Extensions.Logging.ILoggerFactory loggerFactory)
    {
        // Deserialize the request the same way transports do so that params are backed by parsed JSON.
        var request = Assert.IsType<JsonRpcRequest>(JsonSerializer.Deserialize<JsonRpcMessage>(
            Encoding.UTF8.GetBytes(requestJson), McpJsonUtilities.DefaultOptions));

        return await InvokeToolAsync(request, loggerFactory);
    }

    internal static async Task<(JsonRpcMessage Response, (CallToolRequestParams Typed, JsonNode? RawParams)? Captured)> InvokeToolAsync(
        JsonRpcRequest request, Microsoft.Extensions.Logging.ILoggerFactory loggerFactory)
    {
        (CallToolRequestParams Typed, JsonNode? RawParams)? captured = null;

        var options = new McpServerOptions
        {
            ProtocolVersion = "2024-11-05",
            Capabilities = new ServerCapabilities { Tools = new() },
        };
        options.Handlers.ListToolsHandler = (_, _) => new ValueTask<ListToolsResult>(new ListToolsResult());
        options.Handlers.CallToolHandler = (context, _) =>
        {
            captured = (context.Params!, ((JsonRpcRequest)context.JsonRpcMessage).Params);
            return new ValueTask<CallToolResult>(new CallToolResult { Content = [new TextContentBlock { Text = "ok" }] });
        };

        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, options, loggerFactory);

        var responseTcs = new TaskCompletionSource<JsonRpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.OnMessageSent = message =>
        {
            if (message is JsonRpcMessageWithId withId && withId.Id == request.Id)
            {
                responseTcs.TrySetResult(message);
            }
        };

        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await transport.SendClientMessageAsync(request, TestContext.Current.CancellationToken);
        var response = await responseTcs.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        await transport.DisposeAsync();
        await runTask.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        return (response, captured);
    }
}

#if NET
/// <summary>
/// Guards against reintroducing an extra full copy of large request <c>_meta</c> payloads when request
/// parameters are deserialized into their typed representation.
/// </summary>
[Collection(nameof(DisableParallelization))]
public class RequestParamsMetaAllocationTests
{
    [Fact]
    public async Task TypedParamsDeserialization_DoesNotCopyLargeMeta()
    {
        const int MetaSize = 4 * 1024 * 1024;
        string json =
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"echo\",\"_meta\":{\"example.com/blob\":\"" +
            new string('a', MetaSize) +
            "\"}}}";
        byte[] utf8 = Encoding.UTF8.GetBytes(json);

        // Warm up the server pipeline so one-time initialization isn't measured.
        await RequestParamsMetaTests.InvokeToolAsync(ParseRequest(utf8), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        var request = ParseRequest(utf8);
        long before = GC.GetTotalAllocatedBytes(precise: true);
        var (response, captured) = await RequestParamsMetaTests.InvokeToolAsync(request, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        Assert.IsType<JsonRpcResponse>(response);
        Assert.NotNull(captured);
        Assert.Equal(MetaSize, captured.Value.Typed.Meta!["example.com/blob"]!.GetValue<string>().Length);

        // Handling the request (excluding the initial message parse) previously copied the entire _meta
        // payload again. Allow generous headroom for unrelated allocations while still catching that copy.
        Assert.True(allocated < MetaSize / 2, $"Handling the request allocated {allocated:N0} bytes.");

        static JsonRpcRequest ParseRequest(byte[] utf8) =>
            Assert.IsType<JsonRpcRequest>(JsonSerializer.Deserialize<JsonRpcMessage>(utf8, McpJsonUtilities.DefaultOptions));
    }
}
#endif
