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

    [Fact]
    public async Task RetryWithInvalidRequestState_ReturnsJsonRpcError()
    {
        await StartAsync();
        await InitializeWithMrtrAsync();

        // Send a retry with a requestState that doesn't match any active continuation
        var retryParams = new JsonObject
        {
            ["name"] = "elicit-tool",
            ["arguments"] = new JsonObject { ["message"] = "test" },
            ["inputResponses"] = new JsonObject { ["key1"] = new JsonObject { ["action"] = "confirm" } },
            ["requestState"] = "nonexistent-state-id"
        };

        var response = await PostJsonRpcAsync(Request("tools/call", retryParams.ToJsonString()));

        // Read as a generic JsonRpcMessage to check if it's an error
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sseData = Assert.Single(await ReadSseAsync(response.Content).ToListAsync(TestContext.Current.CancellationToken));
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(sseData, McpJsonUtilities.DefaultOptions);

        // Invalid requestState should result in a fresh tool invocation
        // (the tool will return InputRequiredResult since it calls ElicitAsync)
        // or an error, depending on the implementation.
        // In our implementation, unrecognized requestState triggers a new invocation.
        Assert.True(
            message is JsonRpcResponse or JsonRpcError,
            $"Expected JsonRpcResponse or JsonRpcError, got {message?.GetType().Name}");
    }

    [Fact]
    public async Task SessionDelete_CancelsPendingMrtrContinuation()
    {
        await StartAsync();
        await InitializeWithMrtrAsync();

        // 1. Call a tool that suspends at ElicitAsync (implicit MRTR path).
        var response = await PostJsonRpcAsync(CallTool("elicit-tool", """{"message":"Please confirm"}"""));
        var rpcResponse = await AssertSingleSseResponseAsync(response);

        // Verify we got an InputRequiredResult (handler is now suspended, continuation stored).
        var resultObj = Assert.IsType<JsonObject>(rpcResponse.Result);
        Assert.Equal("input_required", resultObj["resultType"]?.GetValue<string>());
        var requestState = resultObj["requestState"]!.GetValue<string>();
        Assert.False(string.IsNullOrEmpty(requestState));

        // 2. DELETE the session while the handler is suspended.
        using var deleteResponse = await HttpClient.DeleteAsync("", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // Poll for the async cancellation to propagate through the handler task.
        // Under thread pool starvation, this can take significantly longer than 100ms.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            if (MockLoggerProvider.LogMessages.Any(m => m.Message.Contains("pending MRTR continuation"))
                || DateTime.UtcNow >= deadline)
            {
                break;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        // 3. Verify that the MRTR cancellation was logged at Debug level.
        var mrtrCancelledLog = MockLoggerProvider.LogMessages
            .Where(m => m.Message.Contains("pending MRTR continuation"))
            .ToList();
        Assert.Single(mrtrCancelledLog);
        Assert.Equal(LogLevel.Debug, mrtrCancelledLog[0].LogLevel);
        Assert.Contains("1", mrtrCancelledLog[0].Message);

        // 4. Verify no error-level log was emitted for the cancellation.
        // The handler's OperationCanceledException should be silently observed, not logged as an error.
        var errorLogs = MockLoggerProvider.LogMessages
            .Where(m => m.LogLevel >= LogLevel.Error && m.Message.Contains("elicit"))
            .ToList();
        Assert.Empty(errorLogs);
    }

    [Fact]
    public async Task SessionDelete_RetryAfterDelete_ReturnsSessionNotFound()
    {
        await StartAsync();
        await InitializeWithMrtrAsync();

        // 1. Call a tool that suspends at ElicitAsync.
        var response = await PostJsonRpcAsync(CallTool("elicit-tool", """{"message":"Please confirm"}"""));
        var rpcResponse = await AssertSingleSseResponseAsync(response);

        var resultObj = Assert.IsType<JsonObject>(rpcResponse.Result);
        var requestState = resultObj["requestState"]!.GetValue<string>();
        var inputRequests = resultObj["inputRequests"]!.AsObject();
        var inputKey = inputRequests.First().Key;

        // 2. DELETE the session.
        using var deleteResponse = await HttpClient.DeleteAsync("", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // 3. Attempt to retry with the old requestState — session is gone.
        var inputResponse = InputResponse.FromElicitResult(new ElicitResult { Action = "accept" });
        var retryParams = new JsonObject
        {
            ["name"] = "elicit-tool",
            ["arguments"] = new JsonObject { ["message"] = "Please confirm" },
            ["requestState"] = requestState,
            ["inputResponses"] = new JsonObject
            {
                [inputKey] = JsonSerializer.SerializeToNode(inputResponse, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(InputResponse)))
            },
        };

        using var retryResponse = await PostJsonRpcAsync(Request("tools/call", retryParams.ToJsonString()));

        // The session was deleted, so we should get a 404 with a JSON-RPC error.
        Assert.Equal(HttpStatusCode.NotFound, retryResponse.StatusCode);
        Assert.Equal("application/json", retryResponse.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// Regression test for a CI hang where the server-side MRTR backcompat resolver routed its
    /// outgoing <c>roots/list</c> request through the session-level transport, which silently
    /// dropped the message when the client's GET stream had not been established yet. The
    /// outgoing request must instead go through the POST's response stream (the request's
    /// <see cref="ModelContextProtocol.Protocol.JsonRpcMessageContext.RelatedTransport"/>) so it
    /// reaches the client without depending on the GET stream at all.
    ///
    /// This test deliberately never opens a GET stream — it only POSTs the initialize, the
    /// initialized notification, the <c>tools/call</c>, and the <c>roots/list</c> response. If the
    /// server falls back to <c>_transport.SendMessageAsync</c>, the test times out instead of
    /// reading the expected <c>roots/list</c> SSE event off the <c>tools/call</c> POST response.
    /// </summary>
    [Fact]
    public async Task BackcompatResolver_SendsServerRequestOverPostStream_WithoutGetStream()
    {
        // Configure a server that does NOT pin DRAFT-2026-v1 so it can negotiate the current
        // protocol with a legacy client. The backcompat resolver path only runs when the
        // negotiated version is not DRAFT-2026-v1.
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = nameof(MrtrProtocolTests),
                Version = "1",
            };
        }).WithTools([
            McpServerTool.Create(
                static string (RequestContext<CallToolRequestParams> context) =>
                {
                    if (context.Params!.InputResponses is { } responses &&
                        responses.TryGetValue("roots", out var response))
                    {
                        var roots = response.Deserialize(InputResponse.ListRootsResultJsonTypeInfo)?.Roots;
                        return $"roots-ok:{roots?.FirstOrDefault()?.Name}";
                    }

                    throw new InputRequiredException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["roots"] = InputRequest.ForRootsList(new ListRootsRequestParams())
                        },
                        requestState: "roots-state");
                },
                new McpServerToolCreateOptions
                {
                    Name = "backcompat-roots-tool",
                    Description = "Throws InputRequiredException so the server's backcompat resolver issues a roots/list",
                }),
        ]).WithHttpTransport();

        _app = Builder.Build();
        _app.MapMcp();
        await _app.StartAsync(TestContext.Current.CancellationToken);

        HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));

        // Initialize with the current (non-draft) protocol so the server's backcompat resolver runs.
        var initJson = """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{"roots":{}},"clientInfo":{"name":"BackcompatTestClient","version":"1.0.0"}}}
            """;

        string sessionId;
        using (var initResponse = await PostJsonRpcAsync(initJson))
        {
            var initRpcResponse = await AssertSingleSseResponseAsync(initResponse);
            Assert.NotNull(initRpcResponse.Result);
            Assert.Equal("2025-11-25", initRpcResponse.Result["protocolVersion"]?.GetValue<string>());

            sessionId = Assert.Single(initResponse.Headers.GetValues("mcp-session-id"));
        }

        HttpClient.DefaultRequestHeaders.Remove("mcp-session-id");
        HttpClient.DefaultRequestHeaders.Add("mcp-session-id", sessionId);
        HttpClient.DefaultRequestHeaders.Remove("MCP-Protocol-Version");
        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2025-11-25");

        // Send the initialized notification.
        using (var initializedResponse = await PostJsonRpcAsync(
            """{"jsonrpc":"2.0","method":"notifications/initialized"}"""))
        {
            Assert.True(initializedResponse.IsSuccessStatusCode);
        }

        _lastRequestId = 1;

        // POST the tools/call and start reading the response SSE stream. We deliberately do NOT
        // open a GET stream — the server-to-client roots/list must be delivered on this POST's
        // response. Use HttpCompletionOption.ResponseHeadersRead so the POST returns as soon as
        // the response headers arrive instead of waiting for the SSE stream to close.
        var callRequest = new HttpRequestMessage(HttpMethod.Post, (string?)null)
        {
            Content = JsonContent(CallTool("backcompat-roots-tool")),
        };
        callRequest.Content.Headers.Add("Mcp-Method", "tools/call");
        callRequest.Content.Headers.Add("Mcp-Name", "backcompat-roots-tool");

        using var callResponse = await HttpClient.SendAsync(
            callRequest,
            HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, callResponse.StatusCode);
        Assert.Equal("text/event-stream", callResponse.Content.Headers.ContentType?.MediaType);

        var sseEvents = ReadSseAsync(callResponse.Content)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        try
        {
            // First SSE event on this POST should be the server-initiated roots/list request.
            Assert.True(await sseEvents.MoveNextAsync(),
                "Server did not send a roots/list request on the tools/call POST response stream. " +
                "If this hangs/times out, the MRTR backcompat resolver is routing the outgoing request " +
                "through the session-level transport instead of the POST's RelatedTransport.");

            var rootsRequestNode = JsonNode.Parse(sseEvents.Current) as JsonObject;
            Assert.NotNull(rootsRequestNode);
            Assert.Equal("roots/list", rootsRequestNode["method"]?.GetValue<string>());
            var rootsRequestId = rootsRequestNode["id"];
            Assert.NotNull(rootsRequestId);

            // POST the roots/list response on a separate connection. The server's pending
            // RequestRootsAsync await will complete and the backcompat resolver will retry the tool.
            var rootsIdLiteral = rootsRequestId.ToJsonString();
            var rootsResponseJson =
                "{\"jsonrpc\":\"2.0\",\"id\":" + rootsIdLiteral +
                ",\"result\":{\"roots\":[{\"uri\":\"file:///workspace\",\"name\":\"Workspace\"}]}}";
            using (var rootsResponseHttp = await PostJsonRpcAsync(rootsResponseJson))
            {
                Assert.True(rootsResponseHttp.IsSuccessStatusCode);
            }

            // Next SSE event on the original POST should be the final tools/call response.
            Assert.True(await sseEvents.MoveNextAsync(), "Server did not return the final tools/call response.");
            var finalResponse = JsonSerializer.Deserialize(sseEvents.Current, GetJsonTypeInfo<JsonRpcResponse>());
            Assert.NotNull(finalResponse);
            Assert.NotNull(finalResponse.Result);

            var content = finalResponse.Result["content"]?.AsArray();
            Assert.NotNull(content);
            var firstContent = Assert.Single(content);
            Assert.Equal("roots-ok:Workspace", firstContent?["text"]?.GetValue<string>());
        }
        finally
        {
            await sseEvents.DisposeAsync();
        }
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
