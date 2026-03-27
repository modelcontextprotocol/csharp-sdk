using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Client;

/// <summary>
/// Edge-case and guardrail tests for MRTR over in-memory pipe transport. These focus on
/// scenarios not easily covered by <see cref="T:ModelContextProtocol.AspNetCore.Tests.MapMcpTests"/>
/// which provides broad happy-path coverage across StreamableHttp, SSE, and Stateless transports.
/// </summary>
public class MrtrIntegrationTests : ClientServerTestBase
{
    private readonly ServerMessageTracker _messageTracker = new();

    public MrtrIntegrationTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
        services.Configure<McpServerOptions>(options =>
        {
            options.ExperimentalProtocolVersion = "2026-06-XX";
            _messageTracker.AddFilters(options.Filters.Message);
        });

        mcpServerBuilder.WithTools([
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
                    Name = "elicitation-tool",
                    Description = "A tool that requests elicitation from the client"
                }),
            McpServerTool.Create(
                async (McpServer server, CancellationToken ct) =>
                {
                    // Attempt concurrent ElicitAsync + SampleAsync — MrtrContext prevents this.
                    var t1 = server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = "Concurrent elicit",
                        RequestedSchema = new()
                    }, ct).AsTask();

                    var t2 = server.SampleAsync(new CreateMessageRequestParams
                    {
                        Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "Concurrent sample" }] }],
                        MaxTokens = 100
                    }, ct).AsTask();

                    await Task.WhenAll(t1, t2);
                    return "done";
                },
                new McpServerToolCreateOptions
                {
                    Name = "concurrent-tool",
                    Description = "A tool that attempts concurrent elicitation and sampling"
                }),
            McpServerTool.Create(
                (McpServer server) =>
                {
                    // Low-level MRTR: throw IncompleteResultException directly instead of using ElicitAsync.
                    // This should NOT be logged at Error level — it's normal MRTR control flow.
                    throw new IncompleteResultException(new IncompleteResult
                    {
                        InputRequests = new Dictionary<string, InputRequest>
                        {
                            ["input_1"] = InputRequest.ForElicitation(new ElicitRequestParams
                            {
                                Message = "low-level elicit",
                                RequestedSchema = new()
                            })
                        }
                    });
                },
                new McpServerToolCreateOptions
                {
                    Name = "incomplete-result-tool",
                    Description = "A tool that throws IncompleteResultException for low-level MRTR"
                }),
            McpServerTool.Create(
                async (McpServer server, RequestContext<CallToolRequestParams> context, CancellationToken ct) =>
                {
                    var requestState = context.Params!.RequestState;
                    var inputResponses = context.Params!.InputResponses;

                    // Final round: we have the requestState from the IncompleteResultException
                    if (requestState == "got-name" && inputResponses is not null
                        && inputResponses.TryGetValue("age", out var ageResponse))
                    {
                        var age = ageResponse.ElicitationResult?.Content?.FirstOrDefault().Value;
                        // Decode the name from requestState — in a real scenario, requestState
                        // would carry the accumulated state, but here we just verify the flow works.
                        return $"age={age}";
                    }

                    // First round: use high-level ElicitAsync (handler suspends)
                    var nameResult = await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = "What is your name?",
                        RequestedSchema = new()
                    }, ct);

                    var name = nameResult.Content?.FirstOrDefault().Value;

                    // Second round: switch to low-level IncompleteResultException (handler dies)
                    throw new IncompleteResultException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["age"] = InputRequest.ForElicitation(new ElicitRequestParams
                            {
                                Message = $"How old are you, {name}?",
                                RequestedSchema = new()
                            })
                        },
                        requestState: "got-name");
                },
                new McpServerToolCreateOptions
                {
                    Name = "elicit-then-incomplete-result-tool",
                    Description = "A tool that uses high-level ElicitAsync then throws IncompleteResultException"
                }),
            McpServerTool.Create(
                async (McpServer server) =>
                {
                    // Attempt to send a JsonRpcRequest via SendMessageAsync — should always throw
                    // since requests must go through SendRequestAsync for response correlation.
                    try
                    {
                        await server.SendMessageAsync(new JsonRpcRequest
                        {
                            Id = new RequestId(999),
                            Method = RequestMethods.ElicitationCreate,
                            Params = JsonSerializer.SerializeToNode(new ElicitRequestParams
                            {
                                Message = "Bypass attempt",
                                RequestedSchema = new()
                            }, McpJsonUtilities.DefaultOptions)
                        });
                        return "NOT BLOCKED - expected InvalidOperationException";
                    }
                    catch (InvalidOperationException ex)
                    {
                        return $"blocked:{ex.Message}";
                    }
                },
                new McpServerToolCreateOptions
                {
                    Name = "sendmessage-bypass-tool",
                    Description = "A tool that attempts to bypass MRTR via SendMessageAsync"
                })
        ]);
    }

    [Fact]
    public async Task CallToolAsync_BothExperimental_ElicitCompletesViaMrtr()
    {
        // Simplest MRTR success: experimental server + experimental client, one elicitation round.
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
            new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["name"] = JsonSerializer.SerializeToElement("Alice", McpJsonUtilities.DefaultOptions)
                }
            });

        await using var client = await CreateMcpClientForServer(clientOptions);
        Assert.Equal("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("elicitation-tool",
            new Dictionary<string, object?> { ["message"] = "What is your name?" },
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("accept:Alice", text);
        Assert.True(result.IsError is not true);
        _messageTracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task CallToolAsync_ConcurrentElicitAndSample_PropagatesError()
    {
        // MrtrContext only allows one pending request at a time. When a tool handler
        // calls ElicitAsync and SampleAsync concurrently via Task.WhenAll, the second
        // call sees the TCS already completed and throws InvalidOperationException.
        // That exception is caught by the tool error handler and returned as IsError.
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };

        // The first concurrent call (ElicitAsync) produces an IncompleteResult.
        // The client resolves it via this handler, which unblocks the first task.
        // Then Task.WhenAll surfaces the InvalidOperationException from the second task.
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            return new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });
        };
        clientOptions.Handlers.SamplingHandler = (request, progress, ct) =>
        {
            return new ValueTask<CreateMessageResult>(new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = "sampled" }],
                Model = "test-model"
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync("concurrent-tool",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        var errorText = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("concurrent-tool", errorText);
        _messageTracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task CallToolAsync_ElicitThenIncompleteResultException_WorksEndToEnd()
    {
        // Verify that a handler can mix high-level MRTR (ElicitAsync) with low-level MRTR
        // (IncompleteResultException) in a single logical flow. The handler:
        // 1. Calls ElicitAsync (high-level: handler suspends, IncompleteResult returned)
        // 2. Gets the response, then throws IncompleteResultException (low-level: handler dies)
        // 3. On the next retry, a fresh handler invocation processes requestState + inputResponses
        StartServer();
        int elicitationCallCount = 0;

        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            elicitationCallCount++;
            if (request?.Message == "What is your name?")
            {
                return new ValueTask<ElicitResult>(new ElicitResult
                {
                    Action = "accept",
                    Content = new Dictionary<string, JsonElement>
                    {
                        ["name"] = JsonDocument.Parse("\"Alice\"").RootElement.Clone()
                    }
                });
            }

            // Second elicitation from the IncompleteResultException path
            return new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["age"] = JsonDocument.Parse("\"30\"").RootElement.Clone()
                }
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync(
            "elicit-then-incomplete-result-tool",
            cancellationToken: TestContext.Current.CancellationToken);

        // Verify the final result came through correctly
        var content = Assert.Single(result.Content);
        Assert.Equal("age=30", Assert.IsType<TextContentBlock>(content).Text);
        Assert.NotEqual(true, result.IsError);

        // Two elicitations: one from ElicitAsync, one from IncompleteResultException's inputRequests
        Assert.Equal(2, elicitationCallCount);

        // Verify no error-level logs for IncompleteResultException
        Assert.DoesNotContain(MockLoggerProvider.LogMessages, m =>
            m.LogLevel == LogLevel.Error &&
            m.Exception is IncompleteResultException);
        _messageTracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task ClientHandlerException_DuringMrtrInputResolution_SurfacesToCaller()
    {
        // When the CLIENT's elicitation handler throws during MRTR input resolution,
        // the retry never reaches the server — the server's handler remains suspended
        // on ElicitAsync(). The exception should surface to the CallToolAsync caller,
        // and the server's orphaned handler should be cleaned up on disposal.
        // This is a fundamental MRTR limitation: the client has no channel to communicate
        // input resolution failures back to the server.
        StartServer();

        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            throw new InvalidOperationException("Client-side elicitation failure");
        };

        await using var client = await CreateMcpClientForServer(clientOptions);
        Assert.Equal("2026-06-XX", client.NegotiatedProtocolVersion);

        // The client handler throws during input resolution, so the exception
        // escapes ResolveInputRequestAsync and surfaces directly to the caller.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.CallToolAsync("elicitation-tool",
                new Dictionary<string, object?> { ["message"] = "Will fail" },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Client-side elicitation failure", ex.Message);

        // Dispose the server to trigger cleanup of the orphaned MRTR continuation.
        // The server should cancel the handler suspended on ElicitAsync() and log
        // the cancelled continuation at Debug level.
        await Server.DisposeAsync();

        Assert.Contains(MockLoggerProvider.LogMessages, m =>
            m.LogLevel == LogLevel.Debug &&
            m.Message.Contains("Cancelled") &&
            m.Message.Contains("MRTR continuation"));
    }

    [Fact]
    public async Task SendMessageAsync_WithJsonRpcRequest_ThrowsAlways()
    {
        // SendMessageAsync should throw InvalidOperationException if the message is a
        // JsonRpcRequest, regardless of MRTR state. Use SendRequestAsync for requests.
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
            new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync("sendmessage-bypass-tool",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.StartsWith("blocked:", text);
        Assert.Contains("SendMessageAsync", text);
        Assert.Contains("SendRequestAsync", text);
    }

    [Fact]
    public async Task LegacyRequestOnMrtrSession_LogsWarning()
    {
        // This test simulates a non-compliant server that negotiates MRTR
        // but sends legacy elicitation/create JSON-RPC requests instead of
        // using IncompleteResult. The client should handle it but log a warning.
        StartServer(); // Required for base class DisposeAsync cleanup
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
            new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });
        clientOptions.Handlers.SamplingHandler = (request, progress, ct) =>
            new ValueTask<CreateMessageResult>(new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = "sampled" }],
                Model = "test-model"
            });

        // Start the client task — it will send initialize and block waiting for response
        var clientTask = McpClient.CreateAsync(
            new StreamClientTransport(
                clientToServer.Writer.AsStream(),
                serverToClient.Reader.AsStream(),
                LoggerFactory),
            clientOptions,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        // Simulate server: read initialize request, respond with experimental version
        var serverReader = new StreamReader(clientToServer.Reader.AsStream());
        var serverWriter = serverToClient.Writer.AsStream();

        // Read the initialize request from client
        var initLine = await serverReader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(initLine);
        var initRequest = JsonSerializer.Deserialize<JsonRpcRequest>(initLine, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(initRequest);
        Assert.Equal("initialize", initRequest.Method);

        // Respond with experimental protocol version (MRTR negotiated)
        var initResponse = new JsonRpcResponse
        {
            Id = initRequest.Id,
            Result = JsonSerializer.SerializeToNode(new InitializeResult
            {
                ProtocolVersion = "2026-06-XX",
                Capabilities = new ServerCapabilities(),
                ServerInfo = new Implementation { Name = "MockMrtrServer", Version = "1.0" }
            }, McpJsonUtilities.DefaultOptions),
        };
        await WriteJsonRpcAsync(serverWriter, initResponse);

        // Read the initialized notification from client
        var initializedLine = await serverReader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(initializedLine);

        // Client is now connected with MRTR negotiated
        await using var client = await clientTask;
        Assert.Equal("2026-06-XX", client.NegotiatedProtocolVersion);

        // Now simulate the non-compliant server sending a legacy elicitation/create request
        var legacyRequest = new JsonRpcRequest
        {
            Id = new RequestId(42),
            Method = RequestMethods.ElicitationCreate,
            Params = JsonSerializer.SerializeToNode(new ElicitRequestParams
            {
                Message = "Legacy elicitation from non-compliant server",
                RequestedSchema = new()
            }, McpJsonUtilities.DefaultOptions),
        };
        await WriteJsonRpcAsync(serverWriter, legacyRequest);

        // Read the client's response to the legacy request
        var responseLine = await serverReader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(responseLine);
        var clientResponse = JsonSerializer.Deserialize<JsonRpcResponse>(responseLine, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(clientResponse);
        Assert.Equal(new RequestId(42), clientResponse.Id);

        // Verify the client handled the request (returned ElicitResult)
        var elicitResult = JsonSerializer.Deserialize<ElicitResult>(clientResponse.Result, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(elicitResult);
        Assert.Equal("accept", elicitResult.Action);

        // Verify the warning was logged
        Assert.Contains(MockLoggerProvider.LogMessages, m =>
            m.LogLevel == LogLevel.Warning &&
            m.Message.Contains("elicitation/create") &&
            m.Message.Contains("MRTR"));

        // Clean up
        clientToServer.Writer.Complete();
        serverToClient.Writer.Complete();
    }

    [Fact]
    public async Task IncompleteResultOnNonMrtrSession_LogsWarning()
    {
        // This test simulates a non-compliant server that sends an IncompleteResult
        // to a client that did NOT negotiate MRTR. The client should still process it
        // (resilience), but log a warning about the unexpected protocol behavior.
        StartServer(); // Required for base class DisposeAsync cleanup
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        // Client does NOT set ExperimentalProtocolVersion — standard protocol only
        var clientOptions = new McpClientOptions();
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
            new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["confirmed"] = JsonDocument.Parse("\"yes\"").RootElement.Clone()
                }
            });

        // Start the client task — it will send initialize and block waiting for response
        var clientTask = McpClient.CreateAsync(
            new StreamClientTransport(
                clientToServer.Writer.AsStream(),
                serverToClient.Reader.AsStream(),
                LoggerFactory),
            clientOptions,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        var serverReader = new StreamReader(clientToServer.Reader.AsStream());
        var serverWriter = serverToClient.Writer.AsStream();

        // Read the initialize request from client
        var initLine = await serverReader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(initLine);
        var initRequest = JsonSerializer.Deserialize<JsonRpcRequest>(initLine, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(initRequest);
        Assert.Equal("initialize", initRequest.Method);

        // Respond with standard protocol version (no MRTR)
        var initResponse = new JsonRpcResponse
        {
            Id = initRequest.Id,
            Result = JsonSerializer.SerializeToNode(new InitializeResult
            {
                ProtocolVersion = "2025-03-26",
                Capabilities = new ServerCapabilities { Tools = new() },
                ServerInfo = new Implementation { Name = "NonCompliantServer", Version = "1.0" }
            }, McpJsonUtilities.DefaultOptions),
        };
        await WriteJsonRpcAsync(serverWriter, initResponse);

        // Read the initialized notification from client
        var initializedLine = await serverReader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(initializedLine);

        // Client is now connected with standard protocol (no MRTR)
        await using var client = await clientTask;
        Assert.Equal("2025-03-26", client.NegotiatedProtocolVersion);

        // Start a background task to handle the client's tools/call request
        var cancellationToken = TestContext.Current.CancellationToken;
        var serverLoop = Task.Run(async () =>
        {
            // Read tools/call request from client
            var callLine = await serverReader.ReadLineAsync(cancellationToken);
            Assert.NotNull(callLine);
            var callRequest = JsonSerializer.Deserialize<JsonRpcRequest>(callLine, McpJsonUtilities.DefaultOptions);
            Assert.NotNull(callRequest);
            Assert.Equal("tools/call", callRequest.Method);

            // Non-compliant server sends IncompleteResult on standard protocol session!
            var incompleteResult = new JsonObject
            {
                ["result_type"] = "incomplete",
                ["inputRequests"] = new JsonObject
                {
                    ["confirm_1"] = JsonSerializer.SerializeToNode(
                        InputRequest.ForElicitation(new ElicitRequestParams
                        {
                            Message = "Unexpected elicitation from non-compliant server",
                            RequestedSchema = new()
                        }), McpJsonUtilities.DefaultOptions)
                },
                ["requestState"] = "non-mrtr-state"
            };

            var incompleteResponse = new JsonRpcResponse
            {
                Id = callRequest.Id,
                Result = incompleteResult,
            };
            await WriteJsonRpcAsync(serverWriter, incompleteResponse);

            // Read the retry request with inputResponses from client
            var retryLine = await serverReader.ReadLineAsync(cancellationToken);
            Assert.NotNull(retryLine);
            var retryRequest = JsonSerializer.Deserialize<JsonRpcRequest>(retryLine, McpJsonUtilities.DefaultOptions);
            Assert.NotNull(retryRequest);
            Assert.Equal("tools/call", retryRequest.Method);

            // Verify the retry contains inputResponses and requestState
            var retryParams = retryRequest.Params as JsonObject;
            Assert.NotNull(retryParams);
            Assert.NotNull(retryParams["inputResponses"]);
            Assert.Equal("non-mrtr-state", retryParams["requestState"]?.GetValue<string>());

            // Now respond with a normal result
            var normalResult = new JsonRpcResponse
            {
                Id = retryRequest.Id,
                Result = JsonSerializer.SerializeToNode(new CallToolResult
                {
                    Content = [new TextContentBlock { Text = "completed-without-mrtr" }]
                }, McpJsonUtilities.DefaultOptions),
            };
            await WriteJsonRpcAsync(serverWriter, normalResult);
        }, cancellationToken);

        // Client calls the tool — the non-compliant server will send IncompleteResult
        var response = await client.SendRequestAsync(
            new JsonRpcRequest
            {
                Method = "tools/call",
                Params = JsonSerializer.SerializeToNode(new CallToolRequestParams
                {
                    Name = "any-tool",
                }, McpJsonUtilities.DefaultOptions)
            },
            cancellationToken);

        await serverLoop;

        Assert.NotNull(response.Result);
        var result = JsonSerializer.Deserialize<CallToolResult>(response.Result, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(result);
        var content = Assert.Single(result.Content);
        Assert.Equal("completed-without-mrtr", Assert.IsType<TextContentBlock>(content).Text);

        // Verify the warning was logged about IncompleteResult on non-MRTR session
        Assert.Contains(MockLoggerProvider.LogMessages, m =>
            m.LogLevel == LogLevel.Warning &&
            m.Message.Contains("IncompleteResult") &&
            m.Message.Contains("did not negotiate MRTR"));

        // Clean up
        clientToServer.Writer.Complete();
        serverToClient.Writer.Complete();
    }

    private static async Task WriteJsonRpcAsync(Stream writer, JsonRpcMessage message)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes<JsonRpcMessage>(message, McpJsonUtilities.DefaultOptions);
        await writer.WriteAsync(bytes, TestContext.Current.CancellationToken);
        await writer.WriteAsync("\n"u8.ToArray(), TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);
    }
}
