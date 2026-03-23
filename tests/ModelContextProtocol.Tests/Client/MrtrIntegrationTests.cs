using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Client;

/// <summary>
/// Integration tests for the MRTR round-trip flow. These verify that when a server tool
/// calls ElicitAsync/SampleAsync/RequestRootsAsync, the client resolves the input requests
/// via its handlers and retries the original request.
/// </summary>
public class MrtrIntegrationTests : ClientServerTestBase
{
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
        });

        mcpServerBuilder.WithTools([
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
                    Description = "A tool that requests sampling from the client"
                }),
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
                    var result = await server.RequestRootsAsync(new ListRootsRequestParams(), ct);
                    return string.Join(",", result.Roots.Select(r => r.Uri));
                },
                new McpServerToolCreateOptions
                {
                    Name = "roots-tool",
                    Description = "A tool that requests roots from the client"
                }),
            McpServerTool.Create(
                async (McpServer server, CancellationToken ct) =>
                {
                    // First round-trip: elicit a name
                    var nameResult = await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = "What is your name?",
                        RequestedSchema = new()
                    }, ct);

                    // Second round-trip: elicit a greeting preference
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
                    Description = "A tool that elicits twice in sequence"
                }),
            McpServerTool.Create(
                async (string prompt, McpServer server, CancellationToken ct) =>
                {
                    // Sampling + elicitation in sequence
                    var sampleResult = await server.SampleAsync(new CreateMessageRequestParams
                    {
                        Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = prompt }] }],
                        MaxTokens = 100
                    }, ct);

                    var sampleText = sampleResult.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";

                    var elicitResult = await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = $"Confirm: {sampleText}",
                        RequestedSchema = new()
                    }, ct);

                    return $"sample={sampleText},action={elicitResult.Action}";
                },
                new McpServerToolCreateOptions
                {
                    Name = "sample-then-elicit-tool",
                    Description = "A tool that samples then elicits"
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
                })
        ]);
    }

    [Fact]
    public async Task CallToolAsync_WithSamplingTool_ResolvesViaMrtr()
    {
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.SamplingHandler = (request, progress, ct) =>
        {
            var text = request?.Messages[^1].Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            return new ValueTask<CreateMessageResult>(new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = $"Sampled: {text}" }],
                Model = "test-model"
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync("sampling-tool",
            new Dictionary<string, object?> { ["prompt"] = "Hello world" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("Sampled: Hello world", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task CallToolAsync_WithElicitationTool_ResolvesViaMrtr()
    {
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            return new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "confirm",
                Content = new Dictionary<string, JsonElement>
                {
                    ["answer"] = JsonDocument.Parse("\"yes\"").RootElement.Clone()
                }
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync("elicitation-tool",
            new Dictionary<string, object?> { ["message"] = "Do you agree?" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("confirm:yes", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task CallToolAsync_WithRootsTool_ResolvesViaMrtr()
    {
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.RootsHandler = (request, ct) =>
        {
            return new ValueTask<ListRootsResult>(new ListRootsResult
            {
                Roots = [new Root { Uri = "file:///project", Name = "Project" }]
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync("roots-tool",
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("file:///project", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task CallToolAsync_WithMultipleElicitations_ResolvesMultipleMrtrRoundTrips()
    {
        StartServer();
        int callCount = 0;
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            var count = Interlocked.Increment(ref callCount);
            string value = count == 1 ? "Alice" : "Hello";
            return new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "confirm",
                Content = new Dictionary<string, JsonElement>
                {
                    ["answer"] = JsonDocument.Parse($"\"{value}\"").RootElement.Clone()
                }
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync("multi-elicit-tool",
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("Hello Alice!", Assert.IsType<TextContentBlock>(content).Text);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task CallToolAsync_WithSamplingThenElicitation_ResolvesSequentialMrtrRoundTrips()
    {
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.SamplingHandler = (request, progress, ct) =>
        {
            return new ValueTask<CreateMessageResult>(new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = "AI response" }],
                Model = "test-model"
            });
        };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            return new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync("sample-then-elicit-tool",
            new Dictionary<string, object?> { ["prompt"] = "Test" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("sample=AI response,action=accept", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task CallToolAsync_ServerExperimentalClientNot_UsesLegacyRequests()
    {
        // Server has ExperimentalProtocolVersion set (from ConfigureServices),
        // but client does NOT. Server negotiates to stable version.
        // ClientSupportsMrtr() returns false → standard JSON-RPC requests.
        StartServer();
        var clientOptions = new McpClientOptions();
        clientOptions.Handlers.SamplingHandler = (request, progress, ct) =>
        {
            var text = request?.Messages[^1].Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            return new ValueTask<CreateMessageResult>(new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = $"Legacy: {text}" }],
                Model = "test-model"
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        // Verify the negotiated version is NOT the experimental one
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("sampling-tool",
            new Dictionary<string, object?> { ["prompt"] = "Hello from legacy client" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("Legacy: Hello from legacy client", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task CallToolAsync_BothExperimental_UsesMrtr()
    {
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.SamplingHandler = (request, progress, ct) =>
        {
            var text = request?.Messages[^1].Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            return new ValueTask<CreateMessageResult>(new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = $"MRTR: {text}" }],
                Model = "test-model"
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        // Verify the negotiated version IS the experimental one
        Assert.Equal("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("sampling-tool",
            new Dictionary<string, object?> { ["prompt"] = "Hello from both" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("MRTR: Hello from both", Assert.IsType<TextContentBlock>(content).Text);
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
}
