using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Regression tests for issue #1754: application-provided <c>resultType</c>, <c>ttlMs</c>, and
/// <c>cacheScope</c> properties must be omitted from legacy responses and preserved on 2026-07-28
/// responses without mutating the application result.
/// </summary>
/// <remarks>
/// These drive the in-process <see cref="ClientServerTestBase"/> client/server pair and inspect the
/// raw JSON-RPC result wire shape (via <see cref="McpSession.SendRequestAsync(JsonRpcRequest, System.Threading.CancellationToken)"/>)
/// so the assertions are about the actual serialized fields rather than deserialized objects.
/// The cases cover normal, cacheable, and immediate alternate typed results.
/// </remarks>
public class ProtocolVersionResultDecorationTests : ClientServerTestBase
{
    private static readonly Implementation s_serverInfo = new() { Name = "result-gating-test", Version = "1" };
    private ListToolsResult _cacheableResult = null!;
    private ListResourcesResult _defaultedCacheableResult = null!;
    private GetPromptResult _normalResult = null!;
    private CallToolResult _immediateAlternateResult = null!;

    public ProtocolVersionResultDecorationTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        _cacheableResult = new()
        {
            ResultType = "handler-cacheable",
            TimeToLive = TimeSpan.FromSeconds(1),
            CacheScope = CacheScope.Private,
        };
        _defaultedCacheableResult = new();
        _normalResult = new() { ResultType = "handler-normal", Description = "normal" };
        _immediateAlternateResult = new()
        {
            ResultType = "handler-immediate",
            Content = [new TextContentBlock { Text = "immediate" }],
        };

        services.Configure<McpServerOptions>(options =>
        {
            options.ServerInfo = s_serverInfo;
            options.Handlers.ListToolsHandler = (_, _) => new(_cacheableResult);
            options.Filters.Request.ListToolsFilters.Add(next => async (request, cancellationToken) =>
            {
                var result = await next(request, cancellationToken);
                result.ResultType = "filter-cacheable";
                result.TimeToLive = TimeSpan.FromMilliseconds(1_234);
                result.CacheScope = CacheScope.Public;
                return result;
            });
            options.Handlers.ListResourcesHandler = (_, _) => new(_defaultedCacheableResult);
            options.Handlers.GetPromptHandler = (_, _) => new(_normalResult);
#pragma warning disable MCPEXP002
            options.Handlers.CallToolWithAlternateHandler = (_, _) =>
                new(new ResultOrAlternate<CallToolResult>(_immediateAlternateResult));
#pragma warning restore MCPEXP002
        });
    }

    [Fact]
    public async Task ToolsList_On2025_11_25Session_OmitsResultTypeAndCacheHints()
    {
        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion });

        Assert.Equal(McpProtocolVersions.November2025ProtocolVersion, client.NegotiatedProtocolVersion);

        var response = await client.SendRequestAsync(
            new JsonRpcRequest { Method = RequestMethods.ToolsList },
            TestContext.Current.CancellationToken);

        AssertExact(new JsonObject { ["tools"] = new JsonArray() }, response.Result);
        Assert.Contains(MockLoggerProvider.LogMessages, message =>
            message.LogLevel == Microsoft.Extensions.Logging.LogLevel.Warning &&
            message.Message.Contains("'resultType, ttlMs, cacheScope'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ToolsCall_On2025_11_25Session_OmitsResultType()
    {
        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion });

        Assert.Equal(McpProtocolVersions.November2025ProtocolVersion, client.NegotiatedProtocolVersion);

        var response = await client.SendRequestAsync(
            new JsonRpcRequest
            {
                Method = RequestMethods.ToolsCall,
                Params = System.Text.Json.JsonSerializer.SerializeToNode(
                    new CallToolRequestParams { Name = "echo" }, McpJsonUtilities.DefaultOptions),
            },
            TestContext.Current.CancellationToken);

        AssertExact(JsonNode.Parse("""{"content":[{"type":"text","text":"immediate"}]}"""), response.Result);
    }

    [Fact]
    public async Task ToolsList_On2026_07_28Session_IncludesResultTypeAndCacheHints()
    {
        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion });

        Assert.Equal(McpProtocolVersions.July2026ProtocolVersion, client.NegotiatedProtocolVersion);

        var response = await client.SendRequestAsync(
            new JsonRpcRequest { Method = RequestMethods.ToolsList },
            TestContext.Current.CancellationToken);

        AssertExact(ModernResult(new JsonObject
        {
            ["resultType"] = "filter-cacheable",
            ["tools"] = new JsonArray(),
            ["ttlMs"] = 1_234,
            ["cacheScope"] = "public",
        }), response.Result);
    }

    [Fact]
    public async Task ToolsCall_On2026_07_28Session_IncludesResultType()
    {
        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion });

        Assert.Equal(McpProtocolVersions.July2026ProtocolVersion, client.NegotiatedProtocolVersion);

        var response = await client.SendRequestAsync(
            new JsonRpcRequest
            {
                Method = RequestMethods.ToolsCall,
                Params = System.Text.Json.JsonSerializer.SerializeToNode(
                    new CallToolRequestParams { Name = "echo" }, McpJsonUtilities.DefaultOptions),
            },
            TestContext.Current.CancellationToken);

        AssertExact(ModernResult(JsonNode.Parse("""{"resultType":"handler-immediate","content":[{"type":"text","text":"immediate"}]}""")!.AsObject()), response.Result);
    }

    [Fact]
    public async Task ResourcesList_On2026_07_28Session_AddsDefaultsWithoutMutatingResult()
    {
        await using var client = await CreateMcpClientForServer(
            new McpClientOptions { ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion });

        var response = await client.SendRequestAsync(
            new JsonRpcRequest { Method = RequestMethods.ResourcesList },
            TestContext.Current.CancellationToken);

        AssertExact(ModernResult(new JsonObject
        {
            ["resultType"] = "complete",
            ["resources"] = new JsonArray(),
            ["ttlMs"] = 0,
            ["cacheScope"] = "private",
        }), response.Result);
        Assert.Null(_defaultedCacheableResult.ResultType);
        Assert.Null(_defaultedCacheableResult.TimeToLive);
        Assert.Null(_defaultedCacheableResult.CacheScope);
    }

    [Theory]
    [InlineData(McpProtocolVersions.November2025ProtocolVersion, false)]
    [InlineData(McpProtocolVersions.July2026ProtocolVersion, true)]
    public async Task PromptsGet_EmitsExactNormalResultShape(string protocolVersion, bool modern)
    {
        await using var client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = protocolVersion });

        var response = await client.SendRequestAsync(
            new JsonRpcRequest
            {
                Method = RequestMethods.PromptsGet,
                Params = JsonSerializer.SerializeToNode(
                    new GetPromptRequestParams { Name = "shared" }, McpJsonUtilities.DefaultOptions),
            },
            TestContext.Current.CancellationToken);

        var expected = JsonNode.Parse(modern
            ? """{"resultType":"handler-normal","description":"normal","messages":[]}"""
            : """{"description":"normal","messages":[]}""")!.AsObject();
        AssertExact(modern ? ModernResult(expected) : expected, response.Result);
    }

    private static JsonObject ModernResult(JsonObject result)
    {
        result["_meta"] = new JsonObject
        {
            [MetaKeys.ServerInfo] = JsonSerializer.SerializeToNode(s_serverInfo, McpJsonUtilities.DefaultOptions),
        };
        return result;
    }

    private static void AssertExact(JsonNode? expected, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(expected, actual), $"Expected: {expected}\nActual: {actual}");
}
