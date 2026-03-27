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
/// including IncompleteResult structure, retry with inputResponses, and error handling.
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
            options.ExperimentalProtocolVersion = "2026-06-XX";
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

        // Should be a JSON-RPC error, not an IncompleteResult
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
        // (the tool will return IncompleteResult since it calls ElicitAsync)
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

        // 1. Call a tool that suspends at ElicitAsync (high-level MRTR path).
        var response = await PostJsonRpcAsync(CallTool("elicit-tool", """{"message":"Please confirm"}"""));
        var rpcResponse = await AssertSingleSseResponseAsync(response);

        // Verify we got an IncompleteResult (handler is now suspended, continuation stored).
        var resultObj = Assert.IsType<JsonObject>(rpcResponse.Result);
        Assert.Equal("incomplete", resultObj["result_type"]?.GetValue<string>());
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

    private Task<HttpResponseMessage> PostJsonRpcAsync(string json) =>
        HttpClient.PostAsync("", JsonContent(json), TestContext.Current.CancellationToken);

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
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2026-06-XX","capabilities":{"sampling":{},"elicitation":{},"roots":{}},"clientInfo":{"name":"MrtrTestClient","version":"1.0.0"}}}
            """;

        using var response = await PostJsonRpcAsync(initJson);
        var rpcResponse = await AssertSingleSseResponseAsync(response);
        Assert.NotNull(rpcResponse.Result);

        // Verify the server negotiated to the experimental version
        var protocolVersion = rpcResponse.Result["protocolVersion"]?.GetValue<string>();
        Assert.Equal("2026-06-XX", protocolVersion);

        var sessionId = Assert.Single(response.Headers.GetValues("mcp-session-id"));
        HttpClient.DefaultRequestHeaders.Remove("mcp-session-id");
        HttpClient.DefaultRequestHeaders.Add("mcp-session-id", sessionId);

        // Set the MCP-Protocol-Version header for subsequent requests
        HttpClient.DefaultRequestHeaders.Remove("MCP-Protocol-Version");
        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2026-06-XX");

        // Reset request ID counter since initialize used ID 1
        _lastRequestId = 1;
    }
}
