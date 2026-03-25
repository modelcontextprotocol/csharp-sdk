using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Tests for the low-level exception-based MRTR API running with Streamable HTTP in stateless mode.
/// Verifies that IncompleteResultException works without session affinity, and that the client
/// resolves multiple concurrent inputRequests and retries correctly.
/// </summary>
public class StatelessMrtrTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{
    private WebApplication? _app;
    private readonly ServerMessageTracker _tracker = new();

    private readonly HttpClientTransportOptions DefaultTransportOptions = new()
    {
        Endpoint = new("http://localhost:5000/"),
        Name = "Stateless MRTR Test Client",
        TransportMode = HttpTransportMode.StreamableHttp,
    };

    private Task StartAsync() => StartAsync(
        options => options.ExperimentalProtocolVersion = "2026-06-XX");

    private async Task StartAsync(Action<McpServerOptions>? configureOptions, params McpServerTool[] additionalTools)
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = nameof(StatelessMrtrTests),
                Version = "1",
            };
            _tracker.AddFilters(options.Filters.Message);
            configureOptions?.Invoke(options);
        })
        .WithHttpTransport(httpOptions =>
        {
            httpOptions.Stateless = true;
        })
        .WithTools([
            // Elicitation-only tool
            McpServerTool.Create(
                static string (RequestContext<CallToolRequestParams> context) =>
                {
                    var inputResponses = context.Params!.InputResponses;
                    if (inputResponses is not null &&
                        inputResponses.TryGetValue("user_input", out var response))
                    {
                        var elicitResult = response.ElicitationResult;
                        return $"elicit-ok:{elicitResult?.Action}";
                    }

                    throw new IncompleteResultException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["user_input"] = InputRequest.ForElicitation(new ElicitRequestParams
                            {
                                Message = "Please confirm",
                                RequestedSchema = new()
                            })
                        },
                        requestState: "elicit-state");
                },
                new McpServerToolCreateOptions
                {
                    Name = "stateless-elicit",
                    Description = "Stateless tool with elicitation"
                }),

            // Sampling-only tool
            McpServerTool.Create(
                static string (RequestContext<CallToolRequestParams> context) =>
                {
                    var inputResponses = context.Params!.InputResponses;
                    if (inputResponses is not null &&
                        inputResponses.TryGetValue("llm_call", out var response))
                    {
                        var samplingResult = response.SamplingResult;
                        var text = samplingResult?.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
                        return $"sample-ok:{text}";
                    }

                    throw new IncompleteResultException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["llm_call"] = InputRequest.ForSampling(new CreateMessageRequestParams
                            {
                                Messages = [new SamplingMessage
                                {
                                    Role = Role.User,
                                    Content = [new TextContentBlock { Text = "Summarize this" }]
                                }],
                                MaxTokens = 100
                            })
                        },
                        requestState: "sample-state");
                },
                new McpServerToolCreateOptions
                {
                    Name = "stateless-sample",
                    Description = "Stateless tool with sampling"
                }),

            // Roots-only tool
            McpServerTool.Create(
                static string (RequestContext<CallToolRequestParams> context) =>
                {
                    var inputResponses = context.Params!.InputResponses;
                    if (inputResponses is not null &&
                        inputResponses.TryGetValue("get_roots", out var response))
                    {
                        var rootsResult = response.RootsResult;
                        var uris = string.Join(",", rootsResult?.Roots.Select(r => r.Uri) ?? []);
                        return $"roots-ok:{uris}";
                    }

                    throw new IncompleteResultException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["get_roots"] = InputRequest.ForRootsList(new ListRootsRequestParams())
                        },
                        requestState: "roots-state");
                },
                new McpServerToolCreateOptions
                {
                    Name = "stateless-roots",
                    Description = "Stateless tool with roots"
                }),

            // All three concurrent: elicitation + sampling + roots in ONE IncompleteResult
            McpServerTool.Create(
                static string (RequestContext<CallToolRequestParams> context) =>
                {
                    var inputResponses = context.Params!.InputResponses;
                    if (inputResponses is not null &&
                        inputResponses.Count == 3 &&
                        inputResponses.ContainsKey("elicit") &&
                        inputResponses.ContainsKey("sample") &&
                        inputResponses.ContainsKey("roots"))
                    {
                        var elicitAction = inputResponses["elicit"].ElicitationResult?.Action;
                        var sampleText = inputResponses["sample"].SamplingResult?
                            .Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
                        var rootUris = string.Join(",",
                            inputResponses["roots"].RootsResult?.Roots.Select(r => r.Uri) ?? []);

                        return $"all-ok:elicit={elicitAction},sample={sampleText},roots={rootUris}";
                    }

                    throw new IncompleteResultException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["elicit"] = InputRequest.ForElicitation(new ElicitRequestParams
                            {
                                Message = "Confirm action",
                                RequestedSchema = new()
                            }),
                            ["sample"] = InputRequest.ForSampling(new CreateMessageRequestParams
                            {
                                Messages = [new SamplingMessage
                                {
                                    Role = Role.User,
                                    Content = [new TextContentBlock { Text = "Generate summary" }]
                                }],
                                MaxTokens = 50
                            }),
                            ["roots"] = InputRequest.ForRootsList(new ListRootsRequestParams())
                        },
                        requestState: "multi-state");
                },
                new McpServerToolCreateOptions
                {
                    Name = "stateless-all-three",
                    Description = "Stateless tool requesting elicit + sample + roots concurrently"
                }),

            // Multi-round-trip tool using requestState to track progress
            McpServerTool.Create(
                static string (RequestContext<CallToolRequestParams> context) =>
                {
                    var requestState = context.Params!.RequestState;
                    var inputResponses = context.Params!.InputResponses;

                    if (requestState == "step-2" && inputResponses is not null)
                    {
                        var confirmation = inputResponses["confirm"].ElicitationResult?.Action;
                        return $"multi-done:confirmed={confirmation}";
                    }

                    if (requestState == "step-1" && inputResponses is not null)
                    {
                        var sampleText = inputResponses["llm"].SamplingResult?
                            .Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;

                        // Second round: ask for confirmation of the LLM result
                        throw new IncompleteResultException(
                            inputRequests: new Dictionary<string, InputRequest>
                            {
                                ["confirm"] = InputRequest.ForElicitation(new ElicitRequestParams
                                {
                                    Message = $"Confirm: {sampleText}",
                                    RequestedSchema = new()
                                })
                            },
                            requestState: "step-2");
                    }

                    // First round: ask the LLM to generate something
                    throw new IncompleteResultException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["llm"] = InputRequest.ForSampling(new CreateMessageRequestParams
                            {
                                Messages = [new SamplingMessage
                                {
                                    Role = Role.User,
                                    Content = [new TextContentBlock { Text = "Generate a plan" }]
                                }],
                                MaxTokens = 100
                            })
                        },
                        requestState: "step-1");
                },
                new McpServerToolCreateOptions
                {
                    Name = "stateless-multi-roundtrip",
                    Description = "Stateless tool with multiple MRTR round-trips"
                }),
            ..additionalTools,
        ]);

        _app = Builder.Build();
        _app.MapMcp();
        await _app.StartAsync(TestContext.Current.CancellationToken);

        HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));
    }

    private Task<McpClient> ConnectAsync(McpClientOptions? clientOptions = null)
        => McpClient.CreateAsync(
            new HttpClientTransport(DefaultTransportOptions, HttpClient, LoggerFactory),
            clientOptions, LoggerFactory, TestContext.Current.CancellationToken);

    private McpClientOptions CreateClientOptionsWithAllHandlers()
    {
        var options = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        options.Handlers.ElicitationHandler = (request, ct) =>
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
        options.Handlers.SamplingHandler = (request, progress, ct) =>
        {
            var prompt = request?.Messages?.LastOrDefault()?.Content
                .OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
            return new ValueTask<CreateMessageResult>(new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = $"LLM:{prompt}" }],
                Model = "test-model"
            });
        };
        options.Handlers.RootsHandler = (request, ct) =>
        {
            return new ValueTask<ListRootsResult>(new ListRootsResult
            {
                Roots = [
                    new Root { Uri = "file:///project", Name = "Project" },
                    new Root { Uri = "file:///data", Name = "Data" }
                ]
            });
        };
        return options;
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
    public async Task Stateless_Elicitation_CompletesViaMrtr()
    {
        await StartAsync();
        var options = CreateClientOptionsWithAllHandlers();

        await using var client = await ConnectAsync(options);

        var result = await client.CallToolAsync("stateless-elicit",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError is not true);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("elicit-ok:accept", text);
        _tracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task Stateless_Sampling_CompletesViaMrtr()
    {
        await StartAsync();
        var options = CreateClientOptionsWithAllHandlers();

        await using var client = await ConnectAsync(options);

        var result = await client.CallToolAsync("stateless-sample",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError is not true);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("sample-ok:LLM:Summarize this", text);
        _tracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task Stateless_Roots_CompletesViaMrtr()
    {
        await StartAsync();
        var options = CreateClientOptionsWithAllHandlers();

        await using var client = await ConnectAsync(options);

        var result = await client.CallToolAsync("stateless-roots",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError is not true);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("roots-ok:file:///project,file:///data", text);
        _tracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task Stateless_AllThreeConcurrent_ClientResolvesAllInputRequests()
    {
        // The key test: a single IncompleteResult with elicitation + sampling + roots
        // inputRequests. The client must resolve all three concurrently (via
        // ResolveInputRequestsAsync), then retry with all three responses in one request.
        await StartAsync();

        var elicitHandlerCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var samplingHandlerCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rootsHandlerCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        options.Handlers.ElicitationHandler = async (request, ct) =>
        {
            elicitHandlerCalled.TrySetResult();
            // Wait for the other handlers to also be called (proves concurrency).
            await Task.WhenAll(
                samplingHandlerCalled.Task.WaitAsync(ct),
                rootsHandlerCalled.Task.WaitAsync(ct));

            return new ElicitResult { Action = "accept" };
        };
        options.Handlers.SamplingHandler = async (request, progress, ct) =>
        {
            samplingHandlerCalled.TrySetResult();
            await Task.WhenAll(
                elicitHandlerCalled.Task.WaitAsync(ct),
                rootsHandlerCalled.Task.WaitAsync(ct));

            return new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = "AI-summary" }],
                Model = "test-model"
            };
        };
        options.Handlers.RootsHandler = async (request, ct) =>
        {
            rootsHandlerCalled.TrySetResult();
            await Task.WhenAll(
                elicitHandlerCalled.Task.WaitAsync(ct),
                samplingHandlerCalled.Task.WaitAsync(ct));

            return new ListRootsResult
            {
                Roots = [new Root { Uri = "file:///workspace", Name = "Workspace" }]
            };
        };

        await using var client = await ConnectAsync(options);

        var result = await client.CallToolAsync("stateless-all-three",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError is not true);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("all-ok:elicit=accept,sample=AI-summary,roots=file:///workspace", text);
        _tracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task Stateless_MultiRoundTrip_CompletesAcrossMultipleRetries()
    {
        // Two rounds of IncompleteResult (step-1: sampling, step-2: elicitation)
        // before the final result. Each round is a full stateless HTTP request.
        await StartAsync();
        int samplingCalls = 0;
        int elicitCalls = 0;

        var options = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        options.Handlers.SamplingHandler = (request, progress, ct) =>
        {
            Interlocked.Increment(ref samplingCalls);
            return new ValueTask<CreateMessageResult>(new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = "Generated plan: do X then Y" }],
                Model = "test-model"
            });
        };
        options.Handlers.ElicitationHandler = (request, ct) =>
        {
            Interlocked.Increment(ref elicitCalls);
            return new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });
        };

        await using var client = await ConnectAsync(options);

        var result = await client.CallToolAsync("stateless-multi-roundtrip",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError is not true);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("multi-done:confirmed=accept", text);

        // Verify both handlers were called (one per round-trip)
        Assert.Equal(1, samplingCalls);
        Assert.Equal(1, elicitCalls);
        _tracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task Stateless_IsMrtrSupported_ReturnsTrue_WhenExperimentalProtocolNegotiated()
    {
        // Regression test: In stateless mode, each request creates a new McpServerImpl that never
        // sees the initialize handshake. The Mcp-Protocol-Version header is flowed via
        // JsonRpcMessageContext.ProtocolVersion so the server can determine MRTR support.
        var isMrtrSupportedTool = McpServerTool.Create(
            static string (McpServer server) => server.IsMrtrSupported.ToString(),
            new McpServerToolCreateOptions
            {
                Name = "check-mrtr",
                Description = "Returns IsMrtrSupported"
            });

        await StartAsync(
            options => options.ExperimentalProtocolVersion = "2026-06-XX",
            isMrtrSupportedTool);

        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };

        await using var client = await ConnectAsync(clientOptions);

        var result = await client.CallToolAsync("check-mrtr",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("True", text);
    }

    [Fact]
    public async Task Stateless_IsMrtrSupported_ReturnsFalse_WhenClientDoesNotOptIn()
    {
        // When the client doesn't set ExperimentalProtocolVersion, IsMrtrSupported should
        // be false even if the server has it configured.
        var isMrtrSupportedTool = McpServerTool.Create(
            static string (McpServer server) => server.IsMrtrSupported.ToString(),
            new McpServerToolCreateOptions
            {
                Name = "check-mrtr",
                Description = "Returns IsMrtrSupported"
            });

        await StartAsync(
            options => options.ExperimentalProtocolVersion = "2026-06-XX",
            isMrtrSupportedTool);

        // Client does NOT set ExperimentalProtocolVersion
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync("check-mrtr",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("False", text);
    }

    [Fact]
    public async Task Stateless_IsMrtrSupportedCheck_ThenThrow_WorksEndToEnd()
    {
        // This mirrors the doc pattern: check IsMrtrSupported, return fallback if false,
        // throw IncompleteResultException if true. This is the exact code from mrtr.md
        // and elicitation.md that was previously untested in stateless mode.
        var docPatternTool = McpServerTool.Create(
            static string (McpServer server, RequestContext<CallToolRequestParams> context) =>
            {
                if (context.Params!.InputResponses?.TryGetValue("user_input", out var response) is true)
                {
                    var elicitResult = response.ElicitationResult;
                    return elicitResult?.Action == "accept"
                        ? $"User accepted: {elicitResult.Content?.FirstOrDefault().Value}"
                        : "User declined.";
                }

                if (!server.IsMrtrSupported)
                {
                    return "This tool requires MRTR support.";
                }

                throw new IncompleteResultException(
                    inputRequests: new Dictionary<string, InputRequest>
                    {
                        ["user_input"] = InputRequest.ForElicitation(new ElicitRequestParams
                        {
                            Message = "Please confirm",
                            RequestedSchema = new()
                        })
                    },
                    requestState: "awaiting-confirmation");
            },
            new McpServerToolCreateOptions
            {
                Name = "doc-pattern-elicit",
                Description = "Mirrors the low-level elicitation doc sample"
            });

        await StartAsync(
            options => options.ExperimentalProtocolVersion = "2026-06-XX",
            docPatternTool);

        // With MRTR client: should complete the full flow
        var mrtrClientOptions = CreateClientOptionsWithAllHandlers();
        mrtrClientOptions.ExperimentalProtocolVersion = "2026-06-XX";

        await using var mrtrClient = await ConnectAsync(mrtrClientOptions);

        var result = await mrtrClient.CallToolAsync("doc-pattern-elicit",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("User accepted: yes", text);
        _tracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task Stateless_IsMrtrSupportedCheck_ReturnsFallback_WhenClientDoesNotOptIn()
    {
        // Same doc pattern tool, but the client doesn't opt in. Should return fallback message.
        var docPatternTool = McpServerTool.Create(
            static string (McpServer server, RequestContext<CallToolRequestParams> context) =>
            {
                if (context.Params!.InputResponses?.TryGetValue("user_input", out var response) is true)
                {
                    var elicitResult = response.ElicitationResult;
                    return elicitResult?.Action == "accept"
                        ? $"User accepted: {elicitResult.Content?.FirstOrDefault().Value}"
                        : "User declined.";
                }

                if (!server.IsMrtrSupported)
                {
                    return "This tool requires MRTR support.";
                }

                throw new IncompleteResultException(
                    inputRequests: new Dictionary<string, InputRequest>
                    {
                        ["user_input"] = InputRequest.ForElicitation(new ElicitRequestParams
                        {
                            Message = "Please confirm",
                            RequestedSchema = new()
                        })
                    },
                    requestState: "awaiting-confirmation");
            },
            new McpServerToolCreateOptions
            {
                Name = "doc-pattern-elicit",
                Description = "Mirrors the low-level elicitation doc sample"
            });

        await StartAsync(
            options => options.ExperimentalProtocolVersion = "2026-06-XX",
            docPatternTool);

        // Client does NOT set ExperimentalProtocolVersion — should get fallback
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync("doc-pattern-elicit",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("This tool requires MRTR support.", text);
    }

    [Fact]
    public async Task Stateless_LoadShedding_RequestStateOnly_CompletesViaMrtr()
    {
        // Tests the load shedding pattern from mrtr.md — requestState-only IncompleteResult
        // without inputRequests. The client should auto-retry with just the requestState.
        var loadSheddingTool = McpServerTool.Create(
            static string (McpServer server, RequestContext<CallToolRequestParams> context) =>
            {
                var requestState = context.Params!.RequestState;
                if (requestState is not null)
                {
                    return $"resumed:{requestState}";
                }

                if (!server.IsMrtrSupported)
                {
                    return "MRTR not supported.";
                }

                throw new IncompleteResultException(requestState: "deferred-work");
            },
            new McpServerToolCreateOptions
            {
                Name = "stateless-loadshed",
                Description = "Load shedding with IsMrtrSupported check"
            });

        await StartAsync(
            options => options.ExperimentalProtocolVersion = "2026-06-XX",
            loadSheddingTool);

        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };

        await using var client = await ConnectAsync(clientOptions);

        var result = await client.CallToolAsync("stateless-loadshed",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("resumed:deferred-work", text);
        _tracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task Stateless_IncompleteResultException_WithoutMrtrClient_ReturnsError()
    {
        // When a tool throws IncompleteResultException in stateless mode and the client doesn't
        // support MRTR, nobody can drive the retry loop: the server can't send JSON-RPC requests
        // to the client (stateless), and the client doesn't recognize IncompleteResult (no MRTR).
        // IsMrtrSupported correctly returns false for this configuration.
        var nativeToolWithoutGuard = McpServerTool.Create(
            static string (McpServer server, RequestContext<CallToolRequestParams> context) =>
            {
                var inputResponses = context.Params!.InputResponses;
                if (inputResponses is not null)
                {
                    return $"resolved:{inputResponses["user_input"].ElicitationResult?.Action}";
                }

                // IsMrtrSupported is false in stateless + non-MRTR, but tool ignores the check
                throw new IncompleteResultException(
                    inputRequests: new Dictionary<string, InputRequest>
                    {
                        ["user_input"] = InputRequest.ForElicitation(new ElicitRequestParams
                        {
                            Message = "Please confirm",
                            RequestedSchema = new()
                        })
                    },
                    requestState: "awaiting-confirmation");
            },
            new McpServerToolCreateOptions
            {
                Name = "native-tool-no-guard",
                Description = "MRTR-native tool that doesn't check IsMrtrSupported"
            });

        await StartAsync(
            options => options.ExperimentalProtocolVersion = "2026-06-XX",
            nativeToolWithoutGuard);

        // Client does NOT opt in to MRTR
        await using var client = await ConnectAsync();

        var ex = await Assert.ThrowsAsync<McpProtocolException>(() =>
            client.CallToolAsync("native-tool-no-guard",
                cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("stateless", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MRTR", ex.Message);
    }
}
