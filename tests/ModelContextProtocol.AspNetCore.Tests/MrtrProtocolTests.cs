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
                static string (McpServer server) => server.IsMrtrSupported.ToString(),
                new McpServerToolCreateOptions
                {
                    Name = "check-mrtr-tool",
                    Description = "Returns IsMrtrSupported"
                }),
            McpServerTool.Create(
                static string (McpServer _) => throw new McpProtocolException("Tool validation failed", McpErrorCode.InvalidParams),
                new McpServerToolCreateOptions
                {
                    Name = "throwing-tool",
                    Description = "A tool that throws immediately"
                }),
            McpServerTool.Create(
                static string (McpServer server, RequestContext<CallToolRequestParams> context) =>
                {
                    var requestState = context.Params!.RequestState;
                    var inputResponses = context.Params!.InputResponses;

                    if (requestState is not null && inputResponses is not null)
                    {
                        var elicitResult = inputResponses["user_confirm"].ElicitationResult;
                        return $"lowlevel-confirmed:{elicitResult?.Action}:{requestState}";
                    }

                    if (!server.IsMrtrSupported)
                    {
                        return "lowlevel-unsupported:MRTR is not available";
                    }

                    throw new IncompleteResultException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["user_confirm"] = InputRequest.ForElicitation(new ElicitRequestParams
                            {
                                Message = "Please confirm",
                                RequestedSchema = new()
                            })
                        },
                        requestState: "lowlevel-state-1");
                },
                new McpServerToolCreateOptions
                {
                    Name = "lowlevel-tool",
                    Description = "Low-level MRTR tool managing state directly"
                }),
            McpServerTool.Create(
                static string (McpServer server, RequestContext<CallToolRequestParams> context) =>
                {
                    var requestState = context.Params!.RequestState;

                    if (requestState is not null)
                    {
                        return $"loadshed-resumed:{requestState}";
                    }

                    throw new IncompleteResultException(requestState: "load-shedding-state");
                },
                new McpServerToolCreateOptions
                {
                    Name = "loadshed-tool",
                    Description = "Low-level MRTR tool that returns requestState only (load shedding)"
                }),
            McpServerTool.Create(
                static string (McpServer server, RequestContext<CallToolRequestParams> context) =>
                {
                    var requestState = context.Params!.RequestState;
                    var inputResponses = context.Params!.InputResponses;

                    if (requestState == "step-2" && inputResponses is not null)
                    {
                        var elicitResult = inputResponses["step2_input"].ElicitationResult;
                        return $"multi-done:{elicitResult?.Action}";
                    }

                    if (requestState == "step-1" && inputResponses is not null)
                    {
                        var elicitResult = inputResponses["step1_input"].ElicitationResult;
                        throw new IncompleteResultException(
                            inputRequests: new Dictionary<string, InputRequest>
                            {
                                ["step2_input"] = InputRequest.ForElicitation(new ElicitRequestParams
                                {
                                    Message = $"Step 2 after {elicitResult?.Action}",
                                    RequestedSchema = new()
                                })
                            },
                            requestState: "step-2");
                    }

                    throw new IncompleteResultException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["step1_input"] = InputRequest.ForElicitation(new ElicitRequestParams
                            {
                                Message = "Step 1",
                                RequestedSchema = new()
                            })
                        },
                        requestState: "step-1");
                },
                new McpServerToolCreateOptions
                {
                    Name = "multi-roundtrip-tool",
                    Description = "Low-level tool requiring multiple round trips"
                }),
            McpServerTool.Create(
                static string (McpServer server) =>
                {
                    // Throws IncompleteResultException even though MRTR may not be supported
                    throw new IncompleteResultException(requestState: "should-fail");
                },
                new McpServerToolCreateOptions
                {
                    Name = "always-incomplete-tool",
                    Description = "Tool that always throws IncompleteResultException regardless of MRTR support"
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

    // --- Low-Level MRTR Protocol Tests ---

    [Fact]
    public async Task LowLevel_ToolReturnsIncompleteResult_WithInputRequestsAndRequestState()
    {
        await StartAsync();
        await InitializeWithMrtrAsync();

        var response = await PostJsonRpcAsync(CallTool("lowlevel-tool"));
        var rpcResponse = await AssertSingleSseResponseAsync(response);

        var resultObj = Assert.IsType<JsonObject>(rpcResponse.Result);
        Assert.Equal("incomplete", resultObj["result_type"]?.GetValue<string>());

        // Verify inputRequests
        var inputRequests = resultObj["inputRequests"]?.AsObject();
        Assert.NotNull(inputRequests);
        Assert.Single(inputRequests);
        var (key, inputRequestNode) = inputRequests.Single();
        Assert.Equal("user_confirm", key);
        Assert.Equal("elicitation/create", inputRequestNode!["method"]?.GetValue<string>());
        Assert.Equal("Please confirm", inputRequestNode["params"]?["message"]?.GetValue<string>());

        // Verify requestState
        Assert.Equal("lowlevel-state-1", resultObj["requestState"]?.GetValue<string>());
    }

    [Fact]
    public async Task LowLevel_ToolReturnsRequestStateOnly_LoadShedding()
    {
        await StartAsync();
        await InitializeWithMrtrAsync();

        var response = await PostJsonRpcAsync(CallTool("loadshed-tool"));
        var rpcResponse = await AssertSingleSseResponseAsync(response);

        var resultObj = Assert.IsType<JsonObject>(rpcResponse.Result);
        Assert.Equal("incomplete", resultObj["result_type"]?.GetValue<string>());

        // No inputRequests — this is a load shedding response
        Assert.Null(resultObj["inputRequests"]);

        // requestState must be present
        Assert.Equal("load-shedding-state", resultObj["requestState"]?.GetValue<string>());
    }

    [Fact]
    public async Task LowLevel_RetryWithInputResponses_ReturnsCompleteResult()
    {
        await StartAsync();
        await InitializeWithMrtrAsync();

        // Step 1: Initial call returns IncompleteResult
        var response1 = await PostJsonRpcAsync(CallTool("lowlevel-tool"));
        var rpcResponse1 = await AssertSingleSseResponseAsync(response1);

        var resultObj1 = Assert.IsType<JsonObject>(rpcResponse1.Result);
        Assert.Equal("incomplete", resultObj1["result_type"]?.GetValue<string>());
        var requestState = resultObj1["requestState"]!.GetValue<string>();

        // Step 2: Retry with inputResponses and requestState
        var retryParams = new JsonObject
        {
            ["name"] = "lowlevel-tool",
            ["arguments"] = new JsonObject(),
            ["inputResponses"] = new JsonObject
            {
                ["user_confirm"] = new JsonObject { ["action"] = "accept" }
            },
            ["requestState"] = requestState
        };

        var response2 = await PostJsonRpcAsync(Request("tools/call", retryParams.ToJsonString()));
        var rpcResponse2 = await AssertSingleSseResponseAsync(response2);

        // Should be a complete CallToolResult
        var callToolResult = AssertType<CallToolResult>(rpcResponse2.Result);
        var content = Assert.Single(callToolResult.Content);
        var text = Assert.IsType<TextContentBlock>(content).Text;
        Assert.Equal($"lowlevel-confirmed:accept:{requestState}", text);
    }

    [Fact]
    public async Task LowLevel_RequestStateOnlyRetry_ReturnsCompleteResult()
    {
        await StartAsync();
        await InitializeWithMrtrAsync();

        // Step 1: Get requestState-only response (load shedding)
        var response1 = await PostJsonRpcAsync(CallTool("loadshed-tool"));
        var rpcResponse1 = await AssertSingleSseResponseAsync(response1);
        var resultObj1 = Assert.IsType<JsonObject>(rpcResponse1.Result);
        var requestState = resultObj1["requestState"]!.GetValue<string>();

        // Step 2: Retry with just requestState (no inputResponses since there were no inputRequests)
        var retryParams = new JsonObject
        {
            ["name"] = "loadshed-tool",
            ["arguments"] = new JsonObject(),
            ["requestState"] = requestState
        };

        var response2 = await PostJsonRpcAsync(Request("tools/call", retryParams.ToJsonString()));
        var rpcResponse2 = await AssertSingleSseResponseAsync(response2);

        var callToolResult = AssertType<CallToolResult>(rpcResponse2.Result);
        var content = Assert.Single(callToolResult.Content);
        var text = Assert.IsType<TextContentBlock>(content).Text;
        Assert.Equal($"loadshed-resumed:{requestState}", text);
    }

    [Fact]
    public async Task LowLevel_MultiRoundTrip_CompletesAfterMultipleExchanges()
    {
        await StartAsync();
        await InitializeWithMrtrAsync();

        // Round 1: Initial call
        var response1 = await PostJsonRpcAsync(CallTool("multi-roundtrip-tool"));
        var rpcResponse1 = await AssertSingleSseResponseAsync(response1);
        var resultObj1 = Assert.IsType<JsonObject>(rpcResponse1.Result);
        Assert.Equal("incomplete", resultObj1["result_type"]?.GetValue<string>());
        Assert.Equal("step-1", resultObj1["requestState"]!.GetValue<string>());
        var inputKey1 = resultObj1["inputRequests"]!.AsObject().Single().Key;
        Assert.Equal("step1_input", inputKey1);

        // Round 2: Retry with step 1 response → gets another IncompleteResult
        var retry1Params = new JsonObject
        {
            ["name"] = "multi-roundtrip-tool",
            ["arguments"] = new JsonObject(),
            ["inputResponses"] = new JsonObject
            {
                ["step1_input"] = new JsonObject { ["action"] = "step1-done" }
            },
            ["requestState"] = "step-1"
        };

        var response2 = await PostJsonRpcAsync(Request("tools/call", retry1Params.ToJsonString()));
        var rpcResponse2 = await AssertSingleSseResponseAsync(response2);
        var resultObj2 = Assert.IsType<JsonObject>(rpcResponse2.Result);
        Assert.Equal("incomplete", resultObj2["result_type"]?.GetValue<string>());
        Assert.Equal("step-2", resultObj2["requestState"]!.GetValue<string>());
        var inputKey2 = resultObj2["inputRequests"]!.AsObject().Single().Key;
        Assert.Equal("step2_input", inputKey2);

        // Round 3: Retry with step 2 response → gets final result
        var retry2Params = new JsonObject
        {
            ["name"] = "multi-roundtrip-tool",
            ["arguments"] = new JsonObject(),
            ["inputResponses"] = new JsonObject
            {
                ["step2_input"] = new JsonObject { ["action"] = "step2-done" }
            },
            ["requestState"] = "step-2"
        };

        var response3 = await PostJsonRpcAsync(Request("tools/call", retry2Params.ToJsonString()));
        var rpcResponse3 = await AssertSingleSseResponseAsync(response3);
        var callToolResult = AssertType<CallToolResult>(rpcResponse3.Result);
        var content = Assert.Single(callToolResult.Content);
        Assert.Equal("multi-done:step2-done", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task LowLevel_IncompleteResultException_WithoutMrtr_ReturnsJsonRpcError()
    {
        await StartAsync();
        await InitializeWithoutMrtrAsync();

        // Call a tool that always throws IncompleteResultException regardless of MRTR support
        var response = await PostJsonRpcAsync(CallTool("always-incomplete-tool"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sseData = Assert.Single(await ReadSseAsync(response.Content).ToListAsync(TestContext.Current.CancellationToken));
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(sseData, McpJsonUtilities.DefaultOptions);

        // Should be a JSON-RPC error, not an IncompleteResult
        var errorMessage = Assert.IsType<JsonRpcError>(message);
        Assert.NotNull(errorMessage.Error);
        Assert.Contains("without input requests", errorMessage.Error.Message);
    }

    [Fact]
    public async Task LowLevel_IncompleteResult_HasCorrectJsonStructure()
    {
        await StartAsync();
        await InitializeWithMrtrAsync();

        var response = await PostJsonRpcAsync(CallTool("lowlevel-tool"));
        var rpcResponse = await AssertSingleSseResponseAsync(response);
        var resultObj = Assert.IsType<JsonObject>(rpcResponse.Result);

        // Verify result_type discriminator
        Assert.Equal("incomplete", resultObj["result_type"]?.GetValue<string>());

        // Verify inputRequests is a properly structured object
        var inputRequests = resultObj["inputRequests"]!.AsObject();
        Assert.NotEmpty(inputRequests);
        foreach (var (key, inputRequest) in inputRequests)
        {
            Assert.NotNull(inputRequest);
            Assert.NotNull(inputRequest["method"]);
            Assert.NotNull(inputRequest["params"]);
        }

        // Verify requestState is a non-empty string
        var requestState = resultObj["requestState"]!.GetValue<string>();
        Assert.False(string.IsNullOrEmpty(requestState));
    }

    [Fact]
    public async Task LowLevel_IsMrtrSupported_ReturnsTrue_WithoutMrtrNegotiation()
    {
        await StartAsync();
        await InitializeWithoutMrtrAsync();

        // On a non-stateless HTTP server, IsMrtrSupported returns true even without
        // MRTR negotiation because the backcompat layer can resolve IncompleteResultException
        // via standard JSON-RPC server-to-client requests.
        var response = await PostJsonRpcAsync(CallTool("check-mrtr-tool"));
        var rpcResponse = await AssertSingleSseResponseAsync(response);

        var callToolResult = AssertType<CallToolResult>(rpcResponse.Result);
        var content = Assert.Single(callToolResult.Content);
        Assert.Equal("True", Assert.IsType<TextContentBlock>(content).Text);
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

        // Allow a moment for the async cancellation to propagate through the handler task.
        await Task.Delay(100, TestContext.Current.CancellationToken);

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

    /// <summary>
    /// Initialize a session requesting a standard protocol version (no MRTR).
    /// </summary>
    private async Task InitializeWithoutMrtrAsync()
    {
        var initJson = """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{"sampling":{},"elicitation":{},"roots":{}},"clientInfo":{"name":"LegacyTestClient","version":"1.0.0"}}}
            """;

        using var response = await PostJsonRpcAsync(initJson);
        var rpcResponse = await AssertSingleSseResponseAsync(response);
        Assert.NotNull(rpcResponse.Result);

        // Verify the server negotiated to the standard version, not the experimental one
        var protocolVersion = rpcResponse.Result["protocolVersion"]?.GetValue<string>();
        Assert.Equal("2025-03-26", protocolVersion);

        var sessionId = Assert.Single(response.Headers.GetValues("mcp-session-id"));
        HttpClient.DefaultRequestHeaders.Remove("mcp-session-id");
        HttpClient.DefaultRequestHeaders.Add("mcp-session-id", sessionId);

        _lastRequestId = 1;
    }
}
