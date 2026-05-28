using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Net;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Protocol-level tests for Multi Round-Trip Requests (MRTR).
/// These tests send raw JSON-RPC requests via HTTP and verify protocol-level behavior
/// including InputRequiredResult structure, retry with inputResponses, and error handling.
/// </summary>
public class MrtrProtocolTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{
    private WebApplication? _app;

    private async Task StartAsync()
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = nameof(MrtrProtocolTests),
                Version = "1",
            };
            options.ProtocolVersion = "DRAFT-2026-v1";
        }).WithTools([
            McpServerTool.Create(
                async (string message, McpServer server, CancellationToken ct) =>
                {
                    var result = await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = message,
                        RequestedSchema = new()
                    }, ct);

                    return $"{result.Action}:{result.Content?.FirstOrDefault().Value}";
                },
                new McpServerToolCreateOptions
                {
                    Name = "elicit-tool",
                    Description = "Elicits from client"
                }),
            McpServerTool.Create(
                static string (McpServer _) => throw new McpProtocolException("Tool validation failed", McpErrorCode.InvalidParams),
                new McpServerToolCreateOptions
                {
                    Name = "throwing-tool",
                    Description = "A tool that throws immediately"
                }),
        ]).WithHttpTransport();

        _app = Builder.Build();
        _app.MapMcp();
        await _app.StartAsync(TestContext.Current.CancellationToken);

        HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
        base.Dispose();
    }

    [Fact]
    public async Task ToolThatThrows_ReturnsJsonRpcError_NotIncompleteResult()
    {
        await StartAsync();
        await InitializeWithMrtrAsync();

        var response = await PostJsonRpcAsync(CallTool("throwing-tool"));

        // Should be a JSON-RPC error, not an InputRequiredResult
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sseData = Assert.Single(await ReadSseAsync(response.Content).ToListAsync(TestContext.Current.CancellationToken));
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(sseData, McpJsonUtilities.DefaultOptions);
        var error = Assert.IsType<JsonRpcError>(message);
        Assert.Equal((int)McpErrorCode.InvalidParams, error.Error.Code);
        Assert.Contains("Tool validation failed", error.Error.Message);
    }

    // --- Helpers ---

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");
    private static JsonTypeInfo<T> GetJsonTypeInfo<T>() => (JsonTypeInfo<T>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T));

    private static async IAsyncEnumerable<string> ReadSseAsync(HttpContent responseContent)
    {
        var responseStream = await responseContent.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        await foreach (var sseItem in SseParser.Create(responseStream).EnumerateAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal("message", sseItem.EventType);
            yield return sseItem.Data;
        }
    }

    private static async Task<JsonRpcResponse> AssertSingleSseResponseAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var sseItem = Assert.Single(await ReadSseAsync(response.Content).ToListAsync(TestContext.Current.CancellationToken));
        var jsonRpcResponse = JsonSerializer.Deserialize(sseItem, GetJsonTypeInfo<JsonRpcResponse>());

        Assert.NotNull(jsonRpcResponse);
        return jsonRpcResponse;
    }

    private Task<HttpResponseMessage> PostJsonRpcAsync(string json)
    {
        var content = JsonContent(json);

        // DRAFT-2026-v1 requires Mcp-Method and (for tools/call) Mcp-Name headers per SEP-2243.
        // Parse the body to derive them and attach to this request only.
        var bodyNode = JsonNode.Parse(json);
        if (bodyNode is JsonObject obj)
        {
            if (obj["method"]?.GetValue<string>() is { } method)
            {
                content.Headers.Add("Mcp-Method", method);

                if (obj["params"] is JsonObject paramsObj)
                {
                    string? mcpName = method switch
                    {
                        "tools/call" or "prompts/get" => paramsObj["name"]?.GetValue<string>(),
                        "resources/read" => paramsObj["uri"]?.GetValue<string>(),
                        _ => null,
                    };
                    if (mcpName is not null)
                    {
                        content.Headers.Add("Mcp-Name", mcpName);
                    }
                }
            }
        }

        return HttpClient.PostAsync("", content, TestContext.Current.CancellationToken);
    }

    private long _lastRequestId = 1;

    private string Request(string method, string parameters = "{}")
    {
        var id = Interlocked.Increment(ref _lastRequestId);
        return $$"""
            {"jsonrpc":"2.0","id":{{id}},"method":"{{method}}","params":{{parameters}}}
            """;
    }

    private string CallTool(string toolName, string arguments = "{}") =>
        Request("tools/call", $$"""
            {"name":"{{toolName}}","arguments":{{arguments}}}
            """);

    /// <summary>
    /// Initialize a session requesting the experimental protocol version that enables MRTR.
    /// </summary>
    private async Task InitializeWithMrtrAsync()
    {
        var initJson = """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"DRAFT-2026-v1","capabilities":{"sampling":{},"elicitation":{},"roots":{}},"clientInfo":{"name":"MrtrTestClient","version":"1.0.0"}}}
            """;

        using var response = await PostJsonRpcAsync(initJson);
        var rpcResponse = await AssertSingleSseResponseAsync(response);
        Assert.NotNull(rpcResponse.Result);

        // Verify the server negotiated to the experimental version
        var protocolVersion = rpcResponse.Result["protocolVersion"]?.GetValue<string>();
        Assert.Equal("DRAFT-2026-v1", protocolVersion);

        var sessionId = Assert.Single(response.Headers.GetValues("mcp-session-id"));
        HttpClient.DefaultRequestHeaders.Remove("mcp-session-id");
        HttpClient.DefaultRequestHeaders.Add("mcp-session-id", sessionId);

        // Set the MCP-Protocol-Version header for subsequent requests
        HttpClient.DefaultRequestHeaders.Remove("MCP-Protocol-Version");
        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", "DRAFT-2026-v1");

        // Reset request ID counter since initialize used ID 1
        _lastRequestId = 1;
    }
}
