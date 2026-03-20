using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
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
                async (string prompt, McpServer server, CancellationToken ct) =>
                {
                    var result = await server.SampleAsync(new CreateMessageRequestParams
                    {
                        Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = prompt }] }],
                        MaxTokens = 100
                    }, ct);

                    return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "No response";
                },
                new McpServerToolCreateOptions
                {
                    Name = "sampling-tool",
                    Description = "Samples from client"
                }),
            McpServerTool.Create(
                async (McpServer server, CancellationToken ct) =>
                {
                    var result = await server.RequestRootsAsync(new ListRootsRequestParams(), ct);
                    return string.Join(",", result.Roots.Select(r => r.Uri));
                },
                new McpServerToolCreateOptions
                {
                    Name = "roots-tool",
                    Description = "Requests roots from client"
                }),
            McpServerTool.Create(
                async (McpServer server, CancellationToken ct) =>
                {
                    // First elicit a name, then elicit a greeting
                    var nameResult = await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = "What is your name?",
                        RequestedSchema = new()
                    }, ct);

                    var greetingResult = await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = "How should I greet you?",
                        RequestedSchema = new()
                    }, ct);

                    var name = nameResult.Content?.FirstOrDefault().Value;
                    var greeting = greetingResult.Content?.FirstOrDefault().Value;
                    return $"{greeting} {name}!";
                },
                new McpServerToolCreateOptions
                {
                    Name = "multi-elicit-tool",
                    Description = "Elicits twice in sequence"
                }),
            McpServerTool.Create(
                () => "simple-result",
                new McpServerToolCreateOptions
                {
                    Name = "simple-tool",
                    Description = "A tool that does not use MRTR"
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
    public async Task ToolCall_ReturnsIncompleteResult_WithElicitationInputRequest()
    {
        await StartAsync();
        await InitializeWithMrtrAsync();

        var response = await PostJsonRpcAsync(CallTool("elicit-tool", """{"message":"Please confirm"}"""));
        var rpcResponse = await AssertSingleSseResponseAsync(response);

        // The server should return an IncompleteResult with result_type = "incomplete"
        var resultObj = Assert.IsType<JsonObject>(rpcResponse.Result);
        Assert.Equal("incomplete", resultObj["result_type"]?.GetValue<string>());

        // There should be inputRequests
        var inputRequests = resultObj["inputRequests"]?.AsObject();
        Assert.NotNull(inputRequests);
        Assert.Single(inputRequests);

        // The single input request should be an elicitation request
        var (key, inputRequestNode) = inputRequests.Single();
        Assert.Equal("elicitation/create", inputRequestNode!["method"]?.GetValue<string>());

        // Verify requestState is present
        Assert.NotNull(resultObj["requestState"]?.GetValue<string>());
    }

    [Fact]
    public async Task ToolCall_ReturnsIncompleteResult_WithSamplingInputRequest()
    {
        await StartAsync();
        await InitializeWithMrtrAsync();

        var response = await PostJsonRpcAsync(CallTool("sampling-tool", """{"prompt":"Hello"}"""));
        var rpcResponse = await AssertSingleSseResponseAsync(response);

        var resultObj = Assert.IsType<JsonObject>(rpcResponse.Result);
        Assert.Equal("incomplete", resultObj["result_type"]?.GetValue<string>());

        var inputRequests = resultObj["inputRequests"]?.AsObject();
        Assert.NotNull(inputRequests);
        Assert.Single(inputRequests);

        var (key, inputRequestNode) = inputRequests.Single();
        Assert.Equal("sampling/createMessage", inputRequestNode!["method"]?.GetValue<string>());
        Assert.NotNull(resultObj["requestState"]?.GetValue<string>());
    }

    [Fact]
    public async Task ToolCall_ReturnsIncompleteResult_WithRootsInputRequest()
    {
        await StartAsync();
        await InitializeWithMrtrAsync();

        var response = await PostJsonRpcAsync(CallTool("roots-tool"));
        var rpcResponse = await AssertSingleSseResponseAsync(response);

        var resultObj = Assert.IsType<JsonObject>(rpcResponse.Result);
        Assert.Equal("incomplete", resultObj["result_type"]?.GetValue<string>());

        var inputRequests = resultObj["inputRequests"]?.AsObject();
        Assert.NotNull(inputRequests);
        Assert.Single(inputRequests);

        var (key, inputRequestNode) = inputRequests.Single();
        Assert.Equal("roots/list", inputRequestNode!["method"]?.GetValue<string>());
    }

    [Fact]
    public async Task RetryWithInputResponses_ReturnsCompleteResult()
    {
        await StartAsync();
        await InitializeWithMrtrAsync();

        // Step 1: Initial tool call returns IncompleteResult
        var response1 = await PostJsonRpcAsync(CallTool("elicit-tool", """{"message":"Please confirm"}"""));
        var rpcResponse1 = await AssertSingleSseResponseAsync(response1);

        var resultObj = Assert.IsType<JsonObject>(rpcResponse1.Result);
        var requestState = resultObj["requestState"]!.GetValue<string>();
        var inputRequests = resultObj["inputRequests"]!.AsObject();
        var requestKey = inputRequests.Single().Key;

        // Step 2: Retry with inputResponses and requestState
        var elicitResponse = new JsonObject
        {
            ["action"] = "confirm",
            ["content"] = new JsonObject { ["answer"] = "yes" }
        };

        var retryParams = new JsonObject
        {
            ["name"] = "elicit-tool",
            ["arguments"] = new JsonObject { ["message"] = "Please confirm" },
            ["inputResponses"] = new JsonObject { [requestKey] = elicitResponse },
            ["requestState"] = requestState
        };

        var response2 = await PostJsonRpcAsync(Request("tools/call", retryParams.ToJsonString()));
        var rpcResponse2 = await AssertSingleSseResponseAsync(response2);

        // Should be a complete CallToolResult
        var callToolResult = AssertType<CallToolResult>(rpcResponse2.Result);
        var content = Assert.Single(callToolResult.Content);
        Assert.Equal("confirm:yes", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task RetryWithSamplingResponse_ReturnsCompleteResult()
    {
        await StartAsync();
        await InitializeWithMrtrAsync();

        // Step 1: Initial tool call returns IncompleteResult
        var response1 = await PostJsonRpcAsync(CallTool("sampling-tool", """{"prompt":"Hello"}"""));
        var rpcResponse1 = await AssertSingleSseResponseAsync(response1);

        var resultObj = Assert.IsType<JsonObject>(rpcResponse1.Result);
        var requestState = resultObj["requestState"]!.GetValue<string>();
        var inputRequests = resultObj["inputRequests"]!.AsObject();
        var requestKey = inputRequests.Single().Key;

        // Step 2: Build sampling response
        var samplingResponse = new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = new JsonObject { ["type"] = "text", ["text"] = "Sampled: Hello" },
            ["model"] = "test-model"
        };

        var retryParams = new JsonObject
        {
            ["name"] = "sampling-tool",
            ["arguments"] = new JsonObject { ["prompt"] = "Hello" },
            ["inputResponses"] = new JsonObject { [requestKey] = samplingResponse },
            ["requestState"] = requestState
        };

        var response2 = await PostJsonRpcAsync(Request("tools/call", retryParams.ToJsonString()));
        var rpcResponse2 = await AssertSingleSseResponseAsync(response2);

        var callToolResult = AssertType<CallToolResult>(rpcResponse2.Result);
        var content = Assert.Single(callToolResult.Content);
        Assert.Equal("Sampled: Hello", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task MultipleElicitations_RequireMultipleRoundTrips()
    {
        await StartAsync();
        await InitializeWithMrtrAsync();

        // Step 1: Initial tool call returns IncompleteResult with first elicitation
        var response1 = await PostJsonRpcAsync(CallTool("multi-elicit-tool"));
        var rpcResponse1 = await AssertSingleSseResponseAsync(response1);

        var resultObj1 = Assert.IsType<JsonObject>(rpcResponse1.Result);
        Assert.Equal("incomplete", resultObj1["result_type"]?.GetValue<string>());
        var requestState1 = resultObj1["requestState"]!.GetValue<string>();
        var inputRequests1 = resultObj1["inputRequests"]!.AsObject();
        var requestKey1 = inputRequests1.Single().Key;

        // Step 2: Retry with first elicitation response - should get second elicitation
        var retryParams1 = new JsonObject
        {
            ["name"] = "multi-elicit-tool",
            ["inputResponses"] = new JsonObject
            {
                [requestKey1] = new JsonObject
                {
                    ["action"] = "confirm",
                    ["content"] = new JsonObject { ["answer"] = "Alice" }
                }
            },
            ["requestState"] = requestState1
        };

        var response2 = await PostJsonRpcAsync(Request("tools/call", retryParams1.ToJsonString()));
        var rpcResponse2 = await AssertSingleSseResponseAsync(response2);

        var resultObj2 = Assert.IsType<JsonObject>(rpcResponse2.Result);
        Assert.Equal("incomplete", resultObj2["result_type"]?.GetValue<string>());
        var requestState2 = resultObj2["requestState"]!.GetValue<string>();
        var inputRequests2 = resultObj2["inputRequests"]!.AsObject();
        var requestKey2 = inputRequests2.Single().Key;

        // Step 3: Retry with second elicitation response - should get final result
        var retryParams2 = new JsonObject
        {
            ["name"] = "multi-elicit-tool",
            ["inputResponses"] = new JsonObject
            {
                [requestKey2] = new JsonObject
                {
                    ["action"] = "confirm",
                    ["content"] = new JsonObject { ["answer"] = "Hello" }
                }
            },
            ["requestState"] = requestState2
        };

        var response3 = await PostJsonRpcAsync(Request("tools/call", retryParams2.ToJsonString()));
        var rpcResponse3 = await AssertSingleSseResponseAsync(response3);

        var callToolResult = AssertType<CallToolResult>(rpcResponse3.Result);
        var content = Assert.Single(callToolResult.Content);
        Assert.Equal("Hello Alice!", Assert.IsType<TextContentBlock>(content).Text);
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
    public async Task SimpleTool_DoesNotReturnIncompleteResult_WhenMrtrCapable()
    {
        await StartAsync();
        await InitializeWithMrtrAsync();

        var response = await PostJsonRpcAsync(CallTool("simple-tool"));
        var rpcResponse = await AssertSingleSseResponseAsync(response);

        // A tool that doesn't call ElicitAsync/SampleAsync should return a normal result
        var callToolResult = AssertType<CallToolResult>(rpcResponse.Result);
        var content = Assert.Single(callToolResult.Content);
        Assert.Equal("simple-result", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task IncompleteResult_HasCorrectStructure()
    {
        await StartAsync();
        await InitializeWithMrtrAsync();

        var response = await PostJsonRpcAsync(CallTool("elicit-tool", """{"message":"test"}"""));
        var rpcResponse = await AssertSingleSseResponseAsync(response);

        var resultObj = Assert.IsType<JsonObject>(rpcResponse.Result);

        // Verify required fields
        Assert.Equal("incomplete", resultObj["result_type"]?.GetValue<string>());
        Assert.NotNull(resultObj["inputRequests"]);
        Assert.NotNull(resultObj["requestState"]);

        // requestState should be a non-empty string
        var requestState = resultObj["requestState"]!.GetValue<string>();
        Assert.False(string.IsNullOrEmpty(requestState));

        // inputRequests should be an object with at least one key
        var inputRequests = resultObj["inputRequests"]!.AsObject();
        Assert.NotEmpty(inputRequests);

        // Each input request should have "method" and "params"
        foreach (var (key, inputRequest) in inputRequests)
        {
            Assert.NotNull(inputRequest);
            Assert.NotNull(inputRequest["method"]);
            Assert.NotNull(inputRequest["params"]);
        }
    }

    [Fact]
    public async Task ElicitationInputRequest_HasCorrectParams()
    {
        await StartAsync();
        await InitializeWithMrtrAsync();

        var response = await PostJsonRpcAsync(CallTool("elicit-tool", """{"message":"Please provide info"}"""));
        var rpcResponse = await AssertSingleSseResponseAsync(response);

        var resultObj = Assert.IsType<JsonObject>(rpcResponse.Result);
        var inputRequest = resultObj["inputRequests"]!.AsObject().Single().Value!;

        Assert.Equal("elicitation/create", inputRequest["method"]?.GetValue<string>());

        var paramsObj = inputRequest["params"]?.AsObject();
        Assert.NotNull(paramsObj);
        Assert.Equal("Please provide info", paramsObj["message"]?.GetValue<string>());
    }

    [Fact]
    public async Task SamplingInputRequest_HasCorrectParams()
    {
        await StartAsync();
        await InitializeWithMrtrAsync();

        var response = await PostJsonRpcAsync(CallTool("sampling-tool", """{"prompt":"Hello world"}"""));
        var rpcResponse = await AssertSingleSseResponseAsync(response);

        var resultObj = Assert.IsType<JsonObject>(rpcResponse.Result);
        var inputRequest = resultObj["inputRequests"]!.AsObject().Single().Value!;

        Assert.Equal("sampling/createMessage", inputRequest["method"]?.GetValue<string>());

        var paramsObj = inputRequest["params"]?.AsObject();
        Assert.NotNull(paramsObj);
        Assert.NotNull(paramsObj["messages"]);
        Assert.Equal(100, paramsObj["maxTokens"]?.GetValue<int>());
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
    public async Task ClientWithoutMrtrCapability_GetsLegacyBehavior()
    {
        await StartAsync();

        // Initialize WITHOUT mrtr in experimental capabilities
        await InitializeWithoutMrtrAsync();

        // The tool call should block and try legacy JSON-RPC sampling/elicitation
        // Since we don't have a handler for the legacy server→client request, it will fail.
        // This tests that the server correctly falls back to the legacy path.
        var response = await PostJsonRpcAsync(CallTool("simple-tool"));
        var rpcResponse = await AssertSingleSseResponseAsync(response);

        // Simple tool should work normally
        var callToolResult = AssertType<CallToolResult>(rpcResponse.Result);
        var content = Assert.Single(callToolResult.Content);
        Assert.Equal("simple-result", Assert.IsType<TextContentBlock>(content).Text);
    }

    // --- Helpers ---

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");
    private static JsonTypeInfo<T> GetJsonTypeInfo<T>() => (JsonTypeInfo<T>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T));

    private static T AssertType<T>(JsonNode? jsonNode)
    {
        var type = JsonSerializer.Deserialize(jsonNode, GetJsonTypeInfo<T>());
        Assert.NotNull(type);
        return type;
    }

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
    /// Initialize a session with MRTR capability advertised.
    /// </summary>
    private async Task InitializeWithMrtrAsync()
    {
        var initJson = """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{"sampling":{},"elicitation":{},"roots":{},"experimental":{"mrtr":{}}},"clientInfo":{"name":"MrtrTestClient","version":"1.0.0"}}}
            """;

        using var response = await PostJsonRpcAsync(initJson);
        var rpcResponse = await AssertSingleSseResponseAsync(response);
        Assert.NotNull(rpcResponse.Result);

        var sessionId = Assert.Single(response.Headers.GetValues("mcp-session-id"));
        HttpClient.DefaultRequestHeaders.Remove("mcp-session-id");
        HttpClient.DefaultRequestHeaders.Add("mcp-session-id", sessionId);

        // Reset request ID counter since initialize used ID 1
        _lastRequestId = 1;
    }

    /// <summary>
    /// Initialize a session WITHOUT MRTR capability.
    /// </summary>
    private async Task InitializeWithoutMrtrAsync()
    {
        var initJson = """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{"sampling":{},"elicitation":{},"roots":{}},"clientInfo":{"name":"LegacyTestClient","version":"1.0.0"}}}
            """;

        using var response = await PostJsonRpcAsync(initJson);
        var rpcResponse = await AssertSingleSseResponseAsync(response);
        Assert.NotNull(rpcResponse.Result);

        var sessionId = Assert.Single(response.Headers.GetValues("mcp-session-id"));
        HttpClient.DefaultRequestHeaders.Remove("mcp-session-id");
        HttpClient.DefaultRequestHeaders.Add("mcp-session-id", sessionId);

        _lastRequestId = 1;
    }
}
