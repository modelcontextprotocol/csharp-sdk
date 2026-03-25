using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;

namespace ModelContextProtocol.AspNetCore.Tests;

public abstract partial class MapMcpTests
{
    private ServerMessageTracker ConfigureExperimentalServer(params Delegate[] tools)
    {
        var messageTracker = new ServerMessageTracker();
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = "ExperimentalServer", Version = "1" };
            options.ExperimentalProtocolVersion = "2026-06-XX";
            messageTracker.AddFilters(options.Filters.Message);
        })
        .WithHttpTransport(ConfigureStateless)
        .WithTools(tools.Select(t => McpServerTool.Create(t)));
        return messageTracker;
    }

    private ServerMessageTracker ConfigureDefaultServer(params Delegate[] tools)
    {
        var messageTracker = new ServerMessageTracker();
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = "DefaultServer", Version = "1" };
            messageTracker.AddFilters(options.Filters.Message);
        })
        .WithHttpTransport(ConfigureStateless)
        .WithTools(tools.Select(t => McpServerTool.Create(t)));
        return messageTracker;
    }

    private Task<McpClient> ConnectExperimentalAsync() =>
        ConnectAsync(configureClient: options =>
        {
            ConfigureMrtrHandlers(options);
            options.ExperimentalProtocolVersion = "2026-06-XX";
        });

    private Task<McpClient> ConnectDefaultAsync() =>
        ConnectAsync(configureClient: ConfigureMrtrHandlers);

    /// <summary>Configures elicitation, sampling, and roots handlers on client options.</summary>
    private static void ConfigureMrtrHandlers(McpClientOptions options)
    {
        options.Handlers.ElicitationHandler = (request, ct) =>
        {
            var message = request?.Message ?? "";
            var answer = message.Contains("name", StringComparison.OrdinalIgnoreCase) ? "Alice"
                : message.Contains("greet", StringComparison.OrdinalIgnoreCase) ? "Hello"
                : "yes";

            return new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["answer"] = JsonDocument.Parse($"\"{answer}\"").RootElement.Clone()
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
    }

    // =====================================================================
    // MRTR tests: experimental (native), backcompat (legacy JSON-RPC), and edge cases.
    // Each test creates its own server with ExperimentalProtocolVersion enabled.
    // =====================================================================

    [McpServerTool(Name = "mrtr-mixed")]
    private static async Task<string> MrtrMixed(McpServer server, RequestContext<CallToolRequestParams> context, CancellationToken ct)
    {
        var state = context.Params!.RequestState;
        var responses = context.Params!.InputResponses;

        // Round 3 entry: confirmation from round 2 available. Transition to await API.
        if (state == "round-2" && responses?.TryGetValue("confirm", out var confirmResponse) == true)
        {
            var confirmation = confirmResponse.ElicitationResult?.Action ?? "unknown";

            // Await API: sequential sampling then elicitation
            var sampleResult = await server.SampleAsync(new CreateMessageRequestParams
            {
                Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "Write greeting" }] }],
                MaxTokens = 100
            }, ct);
            var greeting = sampleResult.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";

            var signoffResult = await server.ElicitAsync(new ElicitRequestParams
            {
                Message = "Sign off as?",
                RequestedSchema = new()
            }, ct);
            var signoff = signoffResult.Action;

            return $"{confirmation}|{greeting}|{signoff}";
        }

        // Round 2 entry: parallel results from round 1 available.
        if (state == "round-1" && responses is not null)
        {
            var name = responses["name"].ElicitationResult?.Content?.FirstOrDefault().Value;
            var weather = responses["weather"].SamplingResult?.Content
                .OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
            var root = responses["roots"].RootsResult?.Roots?.FirstOrDefault()?.Name ?? "";

            // Exception API: single elicitation with requestState
            throw new IncompleteResultException(
                inputRequests: new Dictionary<string, InputRequest>
                {
                    ["confirm"] = InputRequest.ForElicitation(new ElicitRequestParams
                    {
                        Message = $"Confirm {name} in {weather} near {root}?",
                        RequestedSchema = new()
                    })
                },
                requestState: "round-2");
        }

        // Round 1: Exception API with 3 PARALLEL input requests
        throw new IncompleteResultException(
            inputRequests: new Dictionary<string, InputRequest>
            {
                ["name"] = InputRequest.ForElicitation(new ElicitRequestParams
                {
                    Message = "What is your name?",
                    RequestedSchema = new()
                }),
                ["weather"] = InputRequest.ForSampling(new CreateMessageRequestParams
                {
                    Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "Describe the weather" }] }],
                    MaxTokens = 100
                }),
                ["roots"] = InputRequest.ForRootsList(new ListRootsRequestParams())
            },
            requestState: "round-1");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Mrtr_MixedExceptionAndAwaitStyle(bool experimentalServer, bool experimentalClient)
    {
        // Configure server — experimental or default based on parameter.
        var messageTracker = experimentalServer
            ? ConfigureExperimentalServer(MrtrMixed)
            : ConfigureDefaultServer(MrtrMixed);

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        // Configure client — experimental or default based on parameter.
        Action<McpClientOptions> configureClient = experimentalClient
            ? options => { ConfigureMrtrHandlers(options); options.ExperimentalProtocolVersion = "2026-06-XX"; }
            : ConfigureMrtrHandlers;

        if (experimentalServer)
        {
            // Success cases: both exception and await APIs complete.
            // Skip stateless — await API requires handler suspension (stateful only).
            Assert.SkipWhen(Stateless, "Await-style API requires handler suspension (stateful only).");

            await using var client = await ConnectAsync(configureClient: configureClient);

            if (experimentalClient)
            {
                // Both experimental — MRTR end-to-end.
                Assert.Equal("2026-06-XX", client.NegotiatedProtocolVersion);
            }
            else
            {
                // Backcompat — server experimental, client default. Legacy JSON-RPC.
                Assert.Equal("2025-11-25", client.NegotiatedProtocolVersion);
            }

            var result = await client.CallToolAsync("mrtr-mixed",
                cancellationToken: TestContext.Current.CancellationToken);

            var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
            var parts = text.Split('|');
            Assert.Equal(3, parts.Length);

            // confirmation from round 2 elicitation
            Assert.Equal("accept", parts[0]);
            // greeting from await SampleAsync — our test handler returns "LLM:{prompt}"
            Assert.StartsWith("LLM:", parts[1]);
            // signoff from await ElicitAsync
            Assert.Equal("accept", parts[2]);

            if (experimentalClient)
            {
                messageTracker.AssertMrtrUsed();
            }
            else
            {
                messageTracker.AssertMrtrNotUsed();
            }
        }
        else if (Stateless)
        {
            // Stateless + non-experimental: IncompleteResultException cannot be resolved
            // (no MRTR and no stateful backcompat). The server returns an error.
            await using var client = await ConnectAsync(configureClient: configureClient);

            var ex = await Assert.ThrowsAsync<McpProtocolException>(() =>
                client.CallToolAsync("mrtr-mixed",
                    cancellationToken: TestContext.Current.CancellationToken).AsTask());

            Assert.Equal(McpErrorCode.InternalError, ex.ErrorCode);
            Assert.Contains("stateless", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("MRTR", ex.Message);
        }
        else
        {
            // Stateful + non-experimental: backcompat resolves IncompleteResultException
            // via legacy JSON-RPC requests. The tool completes all 3 rounds.
            await using var client = await ConnectAsync(configureClient: configureClient);

            var result = await client.CallToolAsync("mrtr-mixed",
                cancellationToken: TestContext.Current.CancellationToken);

            var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
            var parts = text.Split('|');
            Assert.Equal(3, parts.Length);

            Assert.Equal("accept", parts[0]);
            Assert.StartsWith("LLM:", parts[1]);
            Assert.Equal("accept", parts[2]);

            messageTracker.AssertMrtrNotUsed();
        }
    }

    [McpServerTool(Name = "mrtr-parallel-await")]
    private static async Task<string> MrtrParallelAwait(McpServer server, CancellationToken ct)
    {
        // Start the first await — succeeds with MRTR (creates exchange)
        var elicitTask = server.ElicitAsync(new ElicitRequestParams
        {
            Message = "Parallel elicit",
            RequestedSchema = new()
        }, ct);

        // Start the second await — with MRTR, this throws InvalidOperationException
        // because MrtrContext only supports one pending exchange at a time.
        try
        {
            var sampleTask = server.SampleAsync(new CreateMessageRequestParams
            {
                Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "Parallel sample" }] }],
                MaxTokens = 100
            }, ct);

            // If we get here, both calls succeeded (non-MRTR path)
            var sampleResult = await sampleTask;
            var elicitResult = await elicitTask;
            return $"parallel-ok:{elicitResult.Action}:{sampleResult.Content.OfType<TextContentBlock>().First().Text}";
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Mrtr_ParallelAwaits(bool experimentalServer, bool experimentalClient)
    {
        // Parallel awaits work with regular JSON-RPC but fail with MRTR because
        // MrtrContext only supports one exchange at a time (TrySetResult gate).
        Assert.SkipWhen(Stateless, "Await-style API requires handler suspension (stateful only).");

        var messageTracker = experimentalServer
            ? ConfigureExperimentalServer(MrtrParallelAwait)
            : ConfigureDefaultServer(MrtrParallelAwait);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        // Configure client — experimental or default based on parameter.
        Action<McpClientOptions> configureClient = experimentalClient
            ? options => { ConfigureMrtrHandlers(options); options.ExperimentalProtocolVersion = "2026-06-XX"; }
            : ConfigureMrtrHandlers;
        await using var client = await ConnectAsync(configureClient: configureClient);

        if (experimentalServer && experimentalClient)
        {
            // Both experimental — MRTR active. Parallel awaits hit the MrtrContext
            // concurrency gate and the second call throws InvalidOperationException,
            // which the tool catches and returns as text.
            var result = await client.CallToolAsync("mrtr-parallel-await",
                cancellationToken: TestContext.Current.CancellationToken);

            var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
            Assert.Contains("Concurrent server-to-client requests are not supported", text);
            Assert.True(result.IsError is not true);
        }
        else
        {
            // Non-MRTR: awaits go through regular JSON-RPC — concurrent calls work.
            var result = await client.CallToolAsync("mrtr-parallel-await",
                cancellationToken: TestContext.Current.CancellationToken);

            var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
            Assert.StartsWith("parallel-ok:", text);
            Assert.True(result.IsError is not true);
        }
    }

    [McpServerTool(Name = "mrtr-elicit")]
    private static string MrtrElicit(RequestContext<CallToolRequestParams> context)
    {
        if (context.Params!.InputResponses is { } responses &&
            responses.TryGetValue("user_input", out var response))
        {
            return $"elicit-ok:{response.ElicitationResult?.Action}";
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
    }

    [Fact]
    public async Task Mrtr_LowLevel_Roots_CompletesViaMrtr()
    {
        var messageTracker = ConfigureExperimentalServer(
            [McpServerTool(Name = "mrtr-roots")] (RequestContext<CallToolRequestParams> context) =>
            {
                if (context.Params!.InputResponses is { } responses &&
                    responses.TryGetValue("roots", out var response))
                {
                    var roots = response.RootsResult?.Roots;
                    return $"roots-ok:{string.Join(",", roots?.Select(r => r.Uri) ?? [])}";
                }

                throw new IncompleteResultException(
                    inputRequests: new Dictionary<string, InputRequest>
                    {
                        ["roots"] = InputRequest.ForRootsList(new ListRootsRequestParams())
                    },
                    requestState: "roots-state");
            });
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectExperimentalAsync();

        var result = await client.CallToolAsync("mrtr-roots",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("roots-ok:file:///project,file:///data", text);
        messageTracker.AssertMrtrUsed();
    }

    [McpServerTool(Name = "mrtr-multi")]
    private static string MrtrMulti(RequestContext<CallToolRequestParams> context)
    {
        var requestState = context.Params!.RequestState;
        var inputResponses = context.Params!.InputResponses;

        if (requestState == "round-2" && inputResponses is not null)
        {
            var greeting = inputResponses["greeting"].ElicitationResult?.Action;
            return $"multi-done:greeting={greeting}";
        }

        if (requestState == "round-1" && inputResponses is not null)
        {
            var name = inputResponses["name"].ElicitationResult?.Content?.FirstOrDefault().Value;
            throw new IncompleteResultException(
                inputRequests: new Dictionary<string, InputRequest>
                {
                    ["greeting"] = InputRequest.ForElicitation(new ElicitRequestParams
                    {
                        Message = $"How should I greet {name}?",
                        RequestedSchema = new()
                    })
                },
                requestState: "round-2");
        }

        throw new IncompleteResultException(
            inputRequests: new Dictionary<string, InputRequest>
            {
                ["name"] = InputRequest.ForElicitation(new ElicitRequestParams
                {
                    Message = "What is your name?",
                    RequestedSchema = new()
                })
            },
            requestState: "round-1");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mrtr_MultiRoundTrip_Completes(bool experimentalClient)
    {
        var messageTracker = ConfigureExperimentalServer(MrtrMulti);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        // Configure client — experimental or default based on parameter.
        Action<McpClientOptions> configureClient = experimentalClient
            ? options => { ConfigureMrtrHandlers(options); options.ExperimentalProtocolVersion = "2026-06-XX"; }
            : ConfigureMrtrHandlers;
        await using var client = await ConnectAsync(configureClient: configureClient);

        if (!experimentalClient && Stateless)
        {
            // Stateless without MRTR: IncompleteResultException can't be resolved
            // (no MRTR negotiated and no stateful backcompat path).
            var ex = await Assert.ThrowsAsync<McpProtocolException>(() =>
                client.CallToolAsync("mrtr-multi",
                    cancellationToken: TestContext.Current.CancellationToken).AsTask());
            Assert.Equal(McpErrorCode.InternalError, ex.ErrorCode);
            return;
        }

        var result = await client.CallToolAsync("mrtr-multi",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("multi-done:greeting=accept", text);

        if (experimentalClient)
        {
            Assert.Equal("2026-06-XX", client.NegotiatedProtocolVersion);
            messageTracker.AssertMrtrUsed();
        }
        else
        {
            Assert.Equal("2025-11-25", client.NegotiatedProtocolVersion);
            messageTracker.AssertMrtrNotUsed();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mrtr_IsMrtrSupported(bool experimentalClient)
    {
        ConfigureExperimentalServer([McpServerTool(Name = "mrtr-check")] (McpServer server) => server.IsMrtrSupported.ToString());
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        // Configure client — experimental or default based on parameter.
        Action<McpClientOptions> configureClient = experimentalClient
            ? options => { ConfigureMrtrHandlers(options); options.ExperimentalProtocolVersion = "2026-06-XX"; }
            : ConfigureMrtrHandlers;
        await using var client = await ConnectAsync(configureClient: configureClient);

        var result = await client.CallToolAsync("mrtr-check",
            cancellationToken: TestContext.Current.CancellationToken);

        // IsMrtrSupported is false only when stateless AND client didn't negotiate MRTR
        // (no backcompat path available). All other combos have MRTR or backcompat support.
        var expected = Stateless && !experimentalClient ? "False" : "True";
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal(expected, text);
    }

    [Fact]
    public async Task Mrtr_HighLevel_Roots_CompletesViaMrtr()
    {
        Assert.SkipWhen(Stateless, "High-level API requires stateful handler suspension.");
        var messageTracker = ConfigureExperimentalServer(
            [McpServerTool(Name = "mrtr-hl-roots")] async (McpServer server, CancellationToken ct) =>
            {
                var result = await server.RequestRootsAsync(new ListRootsRequestParams(), ct);
                return string.Join(",", result.Roots.Select(r => r.Uri));
            });
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectExperimentalAsync();

        var result = await client.CallToolAsync("mrtr-hl-roots",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("file:///project,file:///data", text);
        messageTracker.AssertMrtrUsed();
    }

    [McpServerTool(Name = "mrtr-concurrent-three")]
    private static string MrtrConcurrentThree(RequestContext<CallToolRequestParams> context)
    {
        if (context.Params!.InputResponses is { Count: 3 } responses &&
            responses.ContainsKey("elicit") &&
            responses.ContainsKey("sample") &&
            responses.ContainsKey("roots"))
        {
            var elicitAction = responses["elicit"].ElicitationResult?.Action;
            var sampleText = responses["sample"].SamplingResult?
                .Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            var rootUris = string.Join(",",
                responses["roots"].RootsResult?.Roots.Select(r => r.Uri) ?? []);
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
            requestState: "concurrent-state");
    }

    [Fact]
    public async Task Mrtr_ConcurrentThreeInputs_ResolvedSimultaneously()
    {
        var messageTracker = ConfigureExperimentalServer(MrtrConcurrentThree);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        var elicitCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var samplingCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rootsCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var client = await ConnectAsync(configureClient: options =>
        {
            options.ExperimentalProtocolVersion = "2026-06-XX";
            options.Handlers.ElicitationHandler = async (request, ct) =>
            {
                elicitCalled.TrySetResult();
                await Task.WhenAll(samplingCalled.Task.WaitAsync(ct), rootsCalled.Task.WaitAsync(ct));
                return new ElicitResult { Action = "accept" };
            };
            options.Handlers.SamplingHandler = async (request, progress, ct) =>
            {
                samplingCalled.TrySetResult();
                await Task.WhenAll(elicitCalled.Task.WaitAsync(ct), rootsCalled.Task.WaitAsync(ct));
                return new CreateMessageResult
                {
                    Content = [new TextContentBlock { Text = "AI-summary" }],
                    Model = "test-model"
                };
            };
            options.Handlers.RootsHandler = async (request, ct) =>
            {
                rootsCalled.TrySetResult();
                await Task.WhenAll(elicitCalled.Task.WaitAsync(ct), samplingCalled.Task.WaitAsync(ct));
                return new ListRootsResult
                {
                    Roots = [new Root { Uri = "file:///workspace", Name = "Workspace" }]
                };
            };
        });

        var result = await client.CallToolAsync("mrtr-concurrent-three",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("all-ok:elicit=accept,sample=AI-summary,roots=file:///workspace", text);
        messageTracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task Mrtr_Experimental_LoadShedding_RequestStateOnly_CompletesViaMrtr()
    {
        var messageTracker = ConfigureExperimentalServer(
            [McpServerTool(Name = "mrtr-loadshed")] (RequestContext<CallToolRequestParams> context) =>
            {
                if (context.Params!.RequestState is { } state)
                {
                    return $"resumed:{state}";
                }

                // requestState-only IncompleteResultException (no inputRequests)
                throw new IncompleteResultException(requestState: "deferred-work");
            });
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectExperimentalAsync();

        var result = await client.CallToolAsync("mrtr-loadshed",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("resumed:deferred-work", text);
        messageTracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task Mrtr_Backcompat_Roots_ResolvedViaLegacyJsonRpc()
    {
        Assert.SkipWhen(Stateless, "Backcompat requires stateful server for legacy JSON-RPC.");
        var messageTracker = ConfigureExperimentalServer(
            [McpServerTool(Name = "mrtr-roots-backcompat")] (RequestContext<CallToolRequestParams> context) =>
            {
                if (context.Params!.InputResponses is { } responses &&
                    responses.TryGetValue("roots", out var response))
                {
                    var roots = response.RootsResult?.Roots;
                    return $"roots-ok:{roots?.FirstOrDefault()?.Name}";
                }

                throw new IncompleteResultException(
                    inputRequests: new Dictionary<string, InputRequest>
                    {
                        ["roots"] = InputRequest.ForRootsList(new ListRootsRequestParams())
                    },
                    requestState: "roots-state");
            });
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectDefaultAsync();
        Assert.Equal("2025-11-25", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("mrtr-roots-backcompat",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("roots-ok:Project", text);
        messageTracker.AssertMrtrNotUsed();
    }

    [Fact]
    public async Task Mrtr_Backcompat_MultipleInputRequests_ResolvedViaLegacyJsonRpc()
    {
        Assert.SkipWhen(Stateless, "Backcompat requires stateful server for legacy JSON-RPC.");
        var messageTracker = ConfigureExperimentalServer(
            [McpServerTool(Name = "mrtr-multi-input")] (RequestContext<CallToolRequestParams> context) =>
            {
                if (context.Params!.InputResponses is { } responses &&
                    responses.TryGetValue("confirm", out var elicitResponse) &&
                    responses.TryGetValue("summarize", out var sampleResponse))
                {
                    var action = elicitResponse.ElicitationResult?.Action;
                    var text = sampleResponse.SamplingResult?.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
                    return $"both:{action}:{text}";
                }

                throw new IncompleteResultException(
                    inputRequests: new Dictionary<string, InputRequest>
                    {
                        ["confirm"] = InputRequest.ForElicitation(new ElicitRequestParams
                        {
                            Message = "Please confirm",
                            RequestedSchema = new()
                        }),
                        ["summarize"] = InputRequest.ForSampling(new CreateMessageRequestParams
                        {
                            Messages = [new SamplingMessage
                            {
                                Role = Role.User,
                                Content = [new TextContentBlock { Text = "Summarize" }]
                            }],
                            MaxTokens = 100
                        })
                    },
                    requestState: "multi-input-state");
            });
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectDefaultAsync();
        Assert.Equal("2025-11-25", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("mrtr-multi-input",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("both:accept:LLM:Summarize", text);
        messageTracker.AssertMrtrNotUsed();
    }

    [Fact]
    public async Task Mrtr_Backcompat_AlwaysIncomplete_FailsAfterMaxRetries()
    {
        Assert.SkipWhen(Stateless, "Backcompat requires stateful server for legacy JSON-RPC.");
        int elicitCallCount = 0;

        ConfigureExperimentalServer(
            [McpServerTool(Name = "mrtr-always-incomplete")] (RequestContext<CallToolRequestParams> context) =>
            {
                // Always throw — never complete
                throw new IncompleteResultException(
                    inputRequests: new Dictionary<string, InputRequest>
                    {
                        ["confirm"] = InputRequest.ForElicitation(new ElicitRequestParams
                        {
                            Message = "Confirm again",
                            RequestedSchema = new()
                        })
                    },
                    requestState: "infinite");
            });
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectAsync(configureClient: options =>
        {
            ConfigureMrtrHandlers(options);
            var originalHandler = options.Handlers.ElicitationHandler!;
            options.Handlers.ElicitationHandler = (request, ct) =>
            {
                Interlocked.Increment(ref elicitCallCount);
                return originalHandler(request, ct);
            };
        });
        Assert.Equal("2025-11-25", client.NegotiatedProtocolVersion);

        var ex = await Assert.ThrowsAsync<McpProtocolException>(() =>
            client.CallToolAsync("mrtr-always-incomplete",
                cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("exceeded", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("10", ex.Message);
        Assert.Equal(10, elicitCallCount);
    }

    [Fact]
    public async Task Mrtr_Backcompat_EmptyInputRequests_FailsWithError()
    {
        Assert.SkipWhen(Stateless, "Backcompat requires stateful server for legacy JSON-RPC.");
        ConfigureExperimentalServer(
            [McpServerTool(Name = "mrtr-empty-inputs")] (RequestContext<CallToolRequestParams> context) =>
            {
                throw new IncompleteResultException(
                    inputRequests: new Dictionary<string, InputRequest>(),
                    requestState: "empty");
            });
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectDefaultAsync();
        Assert.Equal("2025-11-25", client.NegotiatedProtocolVersion);

        var ex = await Assert.ThrowsAsync<McpProtocolException>(() =>
            client.CallToolAsync("mrtr-empty-inputs",
                cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("without input requests", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mrtr_Backcompat_ClientHandlerThrows_PropagatesError()
    {
        Assert.SkipWhen(Stateless, "Backcompat requires stateful server for legacy JSON-RPC.");

        ConfigureExperimentalServer(MrtrElicit);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectAsync(configureClient: options =>
        {
            ConfigureMrtrHandlers(options);
            options.Handlers.ElicitationHandler = (request, ct) =>
            {
                throw new InvalidOperationException("Client-side elicitation failure");
            };
        });
        Assert.Equal("2025-11-25", client.NegotiatedProtocolVersion);

        // Handler exception propagates through the backcompat JSON-RPC round-trip
        await Assert.ThrowsAsync<McpProtocolException>(() =>
            client.CallToolAsync("mrtr-elicit",
                cancellationToken: TestContext.Current.CancellationToken).AsTask());
    }
}
