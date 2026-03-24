using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for the server's backward-compatible resolution of MRTR-native tools.
/// Verifies that when a tool throws IncompleteResultException and the client doesn't support MRTR,
/// the server resolves input requests via standard JSON-RPC calls (elicitation, sampling, roots)
/// and retries the handler.
/// </summary>
public class MrtrBackcompatTests : ClientServerTestBase
{
    private readonly ServerMessageTracker _tracker = new();

    public MrtrBackcompatTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        // Deliberately NOT setting ExperimentalProtocolVersion on the server.
        services.Configure<McpServerOptions>(options =>
        {
            _tracker.AddOutgoingFilter(options.Filters.Message);
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

            // MRTR-native low-level tools (throw IncompleteResultException).
            // These do NOT check IsMrtrSupported — they rely on the SDK's backcompat layer.
            McpServerTool.Create(
                static string (RequestContext<CallToolRequestParams> context) =>
                {
                    var requestState = context.Params!.RequestState;
                    var inputResponses = context.Params!.InputResponses;

                    if (requestState is not null && inputResponses is not null)
                    {
                        var elicitResult = inputResponses["user_input"].ElicitationResult;
                        return $"completed:{elicitResult?.Action}:{elicitResult?.Content?.FirstOrDefault().Value}";
                    }

                    throw new IncompleteResultException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["user_input"] = InputRequest.ForElicitation(new ElicitRequestParams
                            {
                                Message = "Please provide input",
                                RequestedSchema = new()
                            })
                        },
                        requestState: "state-v1");
                },
                new McpServerToolCreateOptions
                {
                    Name = "native-elicit",
                    Description = "MRTR-native tool using IncompleteResultException for elicitation"
                }),
            McpServerTool.Create(
                static string (RequestContext<CallToolRequestParams> context) =>
                {
                    var requestState = context.Params!.RequestState;
                    var inputResponses = context.Params!.InputResponses;

                    if (requestState is not null && inputResponses is not null)
                    {
                        var samplingResult = inputResponses["llm_request"].SamplingResult;
                        var text = samplingResult?.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
                        return $"sampled:{text}";
                    }

                    throw new IncompleteResultException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["llm_request"] = InputRequest.ForSampling(new CreateMessageRequestParams
                            {
                                Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "Summarize" }] }],
                                MaxTokens = 50
                            })
                        },
                        requestState: "sampling-state");
                },
                new McpServerToolCreateOptions
                {
                    Name = "native-sample",
                    Description = "MRTR-native tool using IncompleteResultException for sampling"
                }),
            McpServerTool.Create(
                static string (RequestContext<CallToolRequestParams> context) =>
                {
                    var inputResponses = context.Params!.InputResponses;

                    if (inputResponses is not null && inputResponses.TryGetValue("step2", out var step2Response))
                    {
                        var step2 = step2Response.ElicitationResult;
                        return $"done:{context.Params.RequestState}:{step2?.Action}";
                    }

                    if (inputResponses is not null && inputResponses.TryGetValue("step1", out var step1Response))
                    {
                        var step1 = step1Response.ElicitationResult;
                        // Second round: ask for confirmation
                        throw new IncompleteResultException(
                            inputRequests: new Dictionary<string, InputRequest>
                            {
                                ["step2"] = InputRequest.ForElicitation(new ElicitRequestParams
                                {
                                    Message = $"Confirm: {step1?.Action}?",
                                    RequestedSchema = new()
                                })
                            },
                            requestState: $"round2:{step1?.Action}");
                    }

                    // First round: ask for input
                    throw new IncompleteResultException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["step1"] = InputRequest.ForElicitation(new ElicitRequestParams
                            {
                                Message = "What do you want?",
                                RequestedSchema = new()
                            })
                        },
                        requestState: "round1");
                },
                new McpServerToolCreateOptions
                {
                    Name = "native-multi-round",
                    Description = "MRTR-native tool requiring multiple rounds"
                }),
            McpServerTool.Create(
                static string (RequestContext<CallToolRequestParams> context) =>
                {
                    var inputResponses = context.Params!.InputResponses;

                    if (inputResponses is not null &&
                        inputResponses.TryGetValue("elicit", out var elicitResponse) &&
                        inputResponses.TryGetValue("sample", out var sampleResponse))
                    {
                        var action = elicitResponse.ElicitationResult?.Action;
                        var text = sampleResponse.SamplingResult?.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
                        return $"both:{action}:{text}";
                    }

                    throw new IncompleteResultException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["elicit"] = InputRequest.ForElicitation(new ElicitRequestParams
                            {
                                Message = "Confirm?",
                                RequestedSchema = new()
                            }),
                            ["sample"] = InputRequest.ForSampling(new CreateMessageRequestParams
                            {
                                Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "Summarize" }] }],
                                MaxTokens = 50
                            })
                        },
                        requestState: "multi-input");
                },
                new McpServerToolCreateOptions
                {
                    Name = "native-multi-input",
                    Description = "MRTR-native tool with multiple InputRequests in one IncompleteResult"
                }),
            McpServerTool.Create(
                static string (RequestContext<CallToolRequestParams> context) =>
                {
                    var inputResponses = context.Params!.InputResponses;

                    if (inputResponses is not null && inputResponses.TryGetValue("roots", out var rootsResponse))
                    {
                        var roots = rootsResponse.RootsResult;
                        var rootName = roots?.Roots.FirstOrDefault()?.Name ?? "none";
                        return $"roots:{rootName}";
                    }

                    throw new IncompleteResultException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["roots"] = InputRequest.ForRootsList(new ListRootsRequestParams())
                        },
                        requestState: "awaiting-roots");
                },
                new McpServerToolCreateOptions
                {
                    Name = "native-roots",
                    Description = "MRTR-native tool requesting roots/list"
                }),
            McpServerTool.Create(
                static string (RequestContext<CallToolRequestParams> context) =>
                {
                    // Always throws IncompleteResultException, never completes.
                    throw new IncompleteResultException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["input"] = InputRequest.ForElicitation(new ElicitRequestParams
                            {
                                Message = "Infinite loop",
                                RequestedSchema = new()
                            })
                        },
                        requestState: $"attempt-{context.Params!.RequestState ?? "0"}");
                },
                new McpServerToolCreateOptions
                {
                    Name = "native-always-incomplete",
                    Description = "MRTR-native tool that never completes"
                }),
            McpServerTool.Create(
                static string (RequestContext<CallToolRequestParams> context) =>
                {
                    // Throws IncompleteResultException with empty inputRequests dict.
                    throw new IncompleteResultException(new IncompleteResult
                    {
                        InputRequests = new Dictionary<string, InputRequest>(),
                        RequestState = "some-state",
                    });
                },
                new McpServerToolCreateOptions
                {
                    Name = "native-empty-inputs",
                    Description = "MRTR-native tool with empty inputRequests"
                }),
            McpServerTool.Create(
                static string (RequestContext<CallToolRequestParams> context) =>
                {
                    var inputResponses = context.Params!.InputResponses;

                    if (inputResponses is not null)
                    {
                        return "should-not-reach";
                    }

                    throw new IncompleteResultException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["user_input"] = InputRequest.ForElicitation(new ElicitRequestParams
                            {
                                Message = "Will fail",
                                RequestedSchema = new()
                            })
                        },
                        requestState: "error-test");
                },
                new McpServerToolCreateOptions
                {
                    Name = "native-elicit-for-error",
                    Description = "MRTR-native tool for testing error propagation"
                }),
        ]);
    }

    [Fact]
    public async Task CallToolAsync_NeitherExperimental_UsesLegacyRequests()
    {
        // Neither client nor server sets ExperimentalProtocolVersion.
        // Server sends standard JSON-RPC sampling/elicitation requests.
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

        // Verify the negotiated version is a standard stable version
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("sampling-tool",
            new Dictionary<string, object?> { ["prompt"] = "Hello" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("Legacy: Hello", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task CallToolAsync_ClientExperimentalServerNot_FallsBackToLegacy()
    {
        // Client requests experimental version, server doesn't recognize it,
        // negotiates to stable. Everything works via legacy path.
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
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

        // Verify the server did NOT negotiate to the experimental version
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("sampling-tool",
            new Dictionary<string, object?> { ["prompt"] = "From exp client" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("Legacy: From exp client", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task CallToolAsync_NeitherExperimental_ElicitationUsesLegacyRequests()
    {
        StartServer();
        var clientOptions = new McpClientOptions();
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            return new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "confirm",
                Content = new Dictionary<string, System.Text.Json.JsonElement>
                {
                    ["response"] = System.Text.Json.JsonDocument.Parse("\"yes\"").RootElement.Clone()
                }
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync("elicitation-tool",
            new Dictionary<string, object?> { ["message"] = "Agree?" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("confirm:yes", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task CallToolAsync_MrtrNativeElicitation_ResolvedViaLegacyJsonRpc()
    {
        // MRTR-native tool (IncompleteResultException) works without MRTR negotiation.
        // The server resolves the elicitation via a standard JSON-RPC call to the client.
        StartServer();
        var clientOptions = new McpClientOptions();
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            Assert.Equal("Please provide input", request?.Message);
            return new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["answer"] = JsonDocument.Parse("\"resolved-via-legacy\"").RootElement.Clone()
                }
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("native-elicit",
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("completed:accept:resolved-via-legacy", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task CallToolAsync_MrtrNativeSampling_ResolvedViaLegacyJsonRpc()
    {
        // MRTR-native tool using sampling resolved via standard JSON-RPC.
        StartServer();
        var clientOptions = new McpClientOptions();
        clientOptions.Handlers.SamplingHandler = (request, progress, ct) =>
        {
            var text = request?.Messages[request.Messages.Count - 1].Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            return new ValueTask<CreateMessageResult>(new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = $"LLM says: {text}" }],
                Model = "test-model"
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("native-sample",
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("sampled:LLM says: Summarize", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task CallToolAsync_MrtrNativeMultiRound_ResolvedViaLegacyJsonRpc()
    {
        // Multi-round MRTR-native tool: two rounds of elicitation, both resolved server-side.
        int elicitCallCount = 0;
        StartServer();
        var clientOptions = new McpClientOptions();
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            elicitCallCount++;
            return new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = elicitCallCount == 1 ? "accept" : "confirm",
                Content = new Dictionary<string, JsonElement>
                {
                    ["value"] = JsonDocument.Parse($"\"{elicitCallCount}\"").RootElement.Clone()
                }
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("native-multi-round",
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("done:round2:accept:confirm", Assert.IsType<TextContentBlock>(content).Text);
        Assert.Equal(2, elicitCallCount);
    }

    [Fact]
    public async Task CallToolAsync_MrtrNativeMultipleInputRequests_ResolvedViaLegacyJsonRpc()
    {
        // Tool throws IncompleteResultException with BOTH elicitation and sampling InputRequests
        // in a single IncompleteResult. The server resolves both via separate legacy JSON-RPC calls.
        StartServer();
        var clientOptions = new McpClientOptions();
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            return new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["answer"] = JsonDocument.Parse("\"yes\"").RootElement.Clone()
                }
            });
        };
        clientOptions.Handlers.SamplingHandler = (request, progress, ct) =>
        {
            return new ValueTask<CreateMessageResult>(new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = "LLM summary" }],
                Model = "test-model"
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("native-multi-input",
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("both:accept:LLM summary", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task CallToolAsync_MrtrNativeRootsList_ResolvedViaLegacyJsonRpc()
    {
        // Tool throws IncompleteResultException with a roots/list InputRequest.
        // The server resolves it via a standard JSON-RPC roots/list call.
        StartServer();
        var clientOptions = new McpClientOptions();
        clientOptions.Handlers.RootsHandler = (request, ct) =>
        {
            return new ValueTask<ListRootsResult>(new ListRootsResult
            {
                Roots = [new Root { Uri = "file:///project", Name = "MyProject" }]
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("native-roots",
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("roots:MyProject", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task CallToolAsync_MrtrNativeAlwaysIncomplete_FailsAfterMaxRetries()
    {
        // Tool always throws IncompleteResultException. The backcompat layer should
        // give up after 10 retry rounds and throw McpException.
        int elicitCallCount = 0;
        StartServer();
        var clientOptions = new McpClientOptions();
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            elicitCallCount++;
            return new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["value"] = JsonDocument.Parse($"\"{elicitCallCount}\"").RootElement.Clone()
                }
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var ex = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.CallToolAsync("native-always-incomplete",
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("exceeded", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("10", ex.Message);
        Assert.Equal(10, elicitCallCount);
    }

    [Fact]
    public async Task CallToolAsync_MrtrNativeEmptyInputRequests_FailsWithMcpException()
    {
        // Tool throws IncompleteResultException with an empty inputRequests dictionary.
        // The backcompat layer should detect this and throw McpException immediately.
        StartServer();
        var clientOptions = new McpClientOptions();

        await using var client = await CreateMcpClientForServer(clientOptions);
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var ex = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.CallToolAsync("native-empty-inputs",
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("without input requests", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallToolAsync_MrtrNativeElicitation_ClientHandlerThrows_PropagatesError()
    {
        // Client's elicitation handler throws. The error should propagate through
        // ResolveInputRequestAsync and surface as an McpException on the client.
        StartServer();
        var clientOptions = new McpClientOptions();
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            throw new InvalidOperationException("Client-side elicitation failure");
        };

        await using var client = await CreateMcpClientForServer(clientOptions);
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        // The client handler's exception message doesn't survive the JSON-RPC round-trip.
        // The server sends elicitation → client handler throws → client returns JSON-RPC error
        // → server receives it as McpProtocolException → server re-throws → becomes JSON-RPC
        // error to the original call → client sees a double-wrapped error.
        var ex = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.CallToolAsync("native-elicit-for-error",
                cancellationToken: TestContext.Current.CancellationToken));
    }
}
