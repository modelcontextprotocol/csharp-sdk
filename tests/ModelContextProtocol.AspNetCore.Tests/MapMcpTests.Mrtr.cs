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
    // Starting with the 2026-07-28 protocol revision, Streamable HTTP no longer supports sessions (SEP-2567):
    // the handler refuses a request when the server opted into sessions (Stateless = false), so a client pinned
    // to that revision downgrades to legacy instead of negotiating 2026-07-28. These MRTR tests therefore can't
    // run on the stateful Streamable HTTP fixture; the same coverage runs on the stateless and legacy-SSE fixtures.
    private const string July2026StatefulStreamableHttpSkipReason =
        "Starting with the 2026-07-28 protocol revision, Streamable HTTP no longer supports sessions (SEP-2567); stateful Streamable HTTP refuses it. Covered by the stateless and SSE fixtures.";

    private ServerMessageTracker ConfigureServer(params Delegate[] tools)
    {
        var messageTracker = new ServerMessageTracker();
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = "MrtrTestServer", Version = "1" };
            // Do not pin a protocol version - let it be negotiated based on what the client requests.
            // 2026-07-28 is in SupportedProtocolVersions, so an opt-in client gets it; others get
            // the latest legacy version.
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
            options.ProtocolVersion = "2026-07-28";
        });

    // The default client now negotiates the 2026-07-28 protocol revision. The legacy
    // JSON-RPC MRTR back-compat resolver only applies to legacy clients, so pin these to the latest legacy version.
    private Task<McpClient> ConnectLegacyAsync() =>
        ConnectAsync(configureClient: options =>
        {
            ConfigureMrtrHandlers(options);
            options.ProtocolVersion = "2025-11-25";
        });

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
    // Each test creates its own server with 2026-07-28 enabled.
    // =====================================================================

    [McpServerTool(Name = "mrtr-mixed")]
    private static async Task<string> MrtrMixed(McpServer server, RequestContext<CallToolRequestParams> context, CancellationToken ct)
    {
        var state = context.Params!.RequestState;
        var responses = context.Params!.InputResponses;

        // Round 3 entry: confirmation from round 2 available. Transition to await API.
        if (state == "round-2" && responses?.TryGetValue("confirm", out var confirmResponse) == true)
        {
            var confirmation = confirmResponse.Deserialize(InputResponse.ElicitResultJsonTypeInfo)?.Action ?? "unknown";

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
            var name = responses["name"].Deserialize(InputResponse.ElicitResultJsonTypeInfo)?.Content?.FirstOrDefault().Value;
            var weather = responses["weather"].Deserialize(InputResponse.CreateMessageResultJsonTypeInfo)?.Content
                .OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
            var root = responses["roots"].Deserialize(InputResponse.ListRootsResultJsonTypeInfo)?.Roots?.FirstOrDefault()?.Name ?? "";

            // Exception API: single elicitation with requestState
            throw new InputRequiredException(
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
        throw new InputRequiredException(
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mrtr_MixedExceptionAndAwaitStyle(bool experimentalClient)
    {
        // the await-style portion of this tool calls server.SampleAsync/ElicitAsync on round 3,
        // which requires server-to-client requests - only available in stateful sessions. Starting with
        // the 2026-07-28 protocol revision (SEP-2567), Streamable HTTP is implicitly stateless, so the
        // experimental-client + HTTP combination cannot resolve the await-style portion. Stdio
        // coverage for this scenario lives in July2026ProtocolBackcompatTests.
        Assert.SkipWhen(experimentalClient, "Await-style MRTR requires session affinity; starting with the 2026-07-28 protocol revision (SEP-2567) Streamable HTTP no longer supports sessions. See July2026ProtocolBackcompatTests for stdio coverage.");

        // The server always supports 2026-07-28 (it's in SupportedProtocolVersions). The
        // client opts in by pinning ProtocolVersion = "2026-07-28"; otherwise it negotiates
        // the latest legacy version and the server falls back to the exception path with
        // legacy JSON-RPC resolution.
        var messageTracker = ConfigureServer(MrtrMixed);

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        Action<McpClientOptions> configureClient = experimentalClient
            ? options => { ConfigureMrtrHandlers(options); options.ProtocolVersion = "2026-07-28"; }
            // ProtocolVersion null now defaults to the 2026-07-28 protocol revision, so pin the legacy client explicitly to keep dual-era coverage.
            : options => { ConfigureMrtrHandlers(options); options.ProtocolVersion = "2025-11-25"; };

        // The await-style portion of this tool calls server.SampleAsync/ElicitAsync on round 3.
        // In stateless mode, those calls succeed only when the request is still open on the same
        // SSE stream - which it is - so the tool runs end-to-end as long as the input requests
        // themselves can be resolved (MRTR client) or replayed via legacy JSON-RPC (stateful + legacy).
        if (Stateless && !experimentalClient)
        {
            // Stateless + legacy client: InputRequiredException cannot be resolved (no MRTR wire
            // and no persistent server instance for the backcompat retry loop). The server returns
            // a JSON-RPC error.
            await using var client = await ConnectAsync(configureClient: configureClient);

            var ex = await Assert.ThrowsAsync<McpProtocolException>(() =>
                client.CallToolAsync("mrtr-mixed",
                    cancellationToken: TestContext.Current.CancellationToken).AsTask());

            Assert.Equal(McpErrorCode.InternalError, ex.ErrorCode);
            Assert.Contains("stateless", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("MRTR", ex.Message);
            return;
        }

        if (Stateless && experimentalClient)
        {
            // Stateless + MRTR client: the await-style portion (server.SampleAsync on round 3)
            // requires handler suspension across requests, which only works in stateful mode.
            // Skip this combination - the await API is documented as stateful-only.
            Assert.SkipWhen(true, "Await-style API requires handler suspension (stateful only).");
            return;
        }

        // Stateful path - both client modes complete all 3 rounds.
        await using var statefulClient = await ConnectAsync(configureClient: configureClient);

        Assert.Equal(experimentalClient ? "2026-07-28" : "2025-11-25",
            statefulClient.NegotiatedProtocolVersion);

        var result = await statefulClient.CallToolAsync("mrtr-mixed",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.True(result.IsError is not true);
        var parts = text.Split('|');
        Assert.Equal(3, parts.Length);
        Assert.Equal("accept", parts[0]);
        Assert.StartsWith("LLM:", parts[1]);
        Assert.Equal("accept", parts[2]);

        if (experimentalClient)
        {
            // Rounds 1-2 use wire-format MRTR (InputRequiredResult), but round 3's await calls
            // still issue legacy elicitation/create + sampling/createMessage requests, so this
            // configuration is mixed-mode.
            messageTracker.AssertMrtrUsedAtLeastOnce();
        }
        else
        {
            messageTracker.AssertMrtrNotUsed();
        }
    }

    [McpServerTool(Name = "mrtr-parallel-await")]
    private static async Task<string> MrtrParallelAwait(McpServer server, CancellationToken ct)
    {
        var elicitTask = server.ElicitAsync(new ElicitRequestParams
        {
            Message = "Parallel elicit",
            RequestedSchema = new()
        }, ct);

        // Start the second await - with MRTR, this throws InvalidOperationException
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mrtr_ParallelAwaits(bool experimentalClient)
    {
        // Parallel awaits work with regular JSON-RPC but fail with MRTR because
        // MrtrContext only supports one exchange at a time (TrySetResult gate).
        Assert.SkipWhen(Stateless, "Await-style API requires handler suspension (stateful only).");
        // Starting with the 2026-07-28 protocol revision (SEP-2567), the server is implicitly stateless for
        // clients on that revision, so parallel-await MRTR can't reach its concurrency gate. Skip the experimental-client
        // case for the same reason as Mrtr_MixedExceptionAndAwaitStyle.
        Assert.SkipWhen(experimentalClient, "Await-style MRTR requires session affinity; starting with the 2026-07-28 protocol revision (SEP-2567) Streamable HTTP no longer supports sessions.");

        ConfigureServer(MrtrParallelAwait);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        Action<McpClientOptions> configureClient = experimentalClient
            ? options => { ConfigureMrtrHandlers(options); options.ProtocolVersion = "2026-07-28"; }
            // ProtocolVersion null now defaults to the 2026-07-28 protocol revision, so pin the legacy client explicitly to keep dual-era coverage.
            : options => { ConfigureMrtrHandlers(options); options.ProtocolVersion = "2025-11-25"; };

        await using var client = await ConnectAsync(configureClient: configureClient);

        if (experimentalClient)
        {
            // MRTR active. Parallel awaits hit the MrtrContext concurrency gate and the second
            // call throws InvalidOperationException, which the tool catches and returns as text.
            Assert.Equal("2026-07-28", client.NegotiatedProtocolVersion);

            var result = await client.CallToolAsync("mrtr-parallel-await",
                cancellationToken: TestContext.Current.CancellationToken);

            var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
            Assert.Contains("Concurrent server-to-client requests are not supported", text);
            Assert.True(result.IsError is not true);
        }
        else
        {
            // Non-MRTR: awaits go through regular JSON-RPC - concurrent calls work.
            Assert.Equal("2025-11-25", client.NegotiatedProtocolVersion);

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
            return $"elicit-ok:{response.Deserialize(InputResponse.ElicitResultJsonTypeInfo)?.Action}";
        }

        throw new InputRequiredException(
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
    public async Task Mrtr_Roots_CompletesViaMrtr()
    {
        Assert.SkipWhen(UseStreamableHttp && !Stateless, July2026StatefulStreamableHttpSkipReason);

        var messageTracker = ConfigureServer(
            [McpServerTool(Name = "mrtr-roots")] (RequestContext<CallToolRequestParams> context) =>
            {
                if (context.Params!.InputResponses is { } responses &&
                    responses.TryGetValue("roots", out var response))
                {
                    var roots = response.Deserialize(InputResponse.ListRootsResultJsonTypeInfo)?.Roots;
                    return $"roots-ok:{string.Join(",", roots?.Select(r => r.Uri) ?? [])}";
                }

                throw new InputRequiredException(
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
        Assert.Equal("2026-07-28", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("mrtr-roots",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("roots-ok:file:///project,file:///data", text);
        Assert.True(result.IsError is not true);
        messageTracker.AssertMrtrUsed();
    }

    [McpServerTool(Name = "mrtr-multi")]
    private static string MrtrMulti(RequestContext<CallToolRequestParams> context)
    {
        var requestState = context.Params!.RequestState;
        var inputResponses = context.Params!.InputResponses;

        if (requestState == "round-2" && inputResponses is not null)
        {
            var greeting = inputResponses["greeting"].Deserialize(InputResponse.ElicitResultJsonTypeInfo)?.Action;
            return $"multi-done:greeting={greeting}";
        }

        if (requestState == "round-1" && inputResponses is not null)
        {
            var name = inputResponses["name"].Deserialize(InputResponse.ElicitResultJsonTypeInfo)?.Content?.FirstOrDefault().Value;
            throw new InputRequiredException(
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

        throw new InputRequiredException(
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
        Assert.SkipWhen(experimentalClient && UseStreamableHttp && !Stateless, July2026StatefulStreamableHttpSkipReason);

        var messageTracker = ConfigureServer(MrtrMulti);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        // Configure client - experimental or default based on parameter.
        Action<McpClientOptions> configureClient = experimentalClient
            ? options => { ConfigureMrtrHandlers(options); options.ProtocolVersion = "2026-07-28"; }
            // ProtocolVersion null now defaults to the 2026-07-28 protocol revision, so pin the legacy client explicitly to keep dual-era coverage.
            : options => { ConfigureMrtrHandlers(options); options.ProtocolVersion = "2025-11-25"; };
        await using var client = await ConnectAsync(configureClient: configureClient);

        if (!experimentalClient && Stateless)
        {
            // Stateless without MRTR: InputRequiredException can't be resolved
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
        Assert.True(result.IsError is not true);

        if (experimentalClient)
        {
            Assert.Equal("2026-07-28", client.NegotiatedProtocolVersion);
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
        Assert.SkipWhen(experimentalClient && UseStreamableHttp && !Stateless, July2026StatefulStreamableHttpSkipReason);

        ConfigureServer([McpServerTool(Name = "mrtr-check")] (McpServer server) => server.IsMrtrSupported.ToString());
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        // Configure client - experimental or default based on parameter.
        Action<McpClientOptions> configureClient = experimentalClient
            ? options => { ConfigureMrtrHandlers(options); options.ProtocolVersion = "2026-07-28"; }
            // ProtocolVersion null now defaults to the 2026-07-28 protocol revision, so pin the legacy client explicitly to keep dual-era coverage.
            : options => { ConfigureMrtrHandlers(options); options.ProtocolVersion = "2025-11-25"; };
        await using var client = await ConnectAsync(configureClient: configureClient);
        Assert.Equal(experimentalClient ? "2026-07-28" : "2025-11-25", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("mrtr-check",
            cancellationToken: TestContext.Current.CancellationToken);

        // IsMrtrSupported is false only when stateless AND client didn't negotiate MRTR
        // (no backcompat path available). All other combos have MRTR or backcompat support.
        var expected = Stateless && !experimentalClient ? "False" : "True";
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal(expected, text);
    }

    [McpServerTool(Name = "mrtr-concurrent-three")]
    private static string MrtrConcurrentThree(RequestContext<CallToolRequestParams> context)
    {
        if (context.Params!.InputResponses is { Count: 3 } responses &&
            responses.ContainsKey("elicit") &&
            responses.ContainsKey("sample") &&
            responses.ContainsKey("roots"))
        {
            var elicitAction = responses["elicit"].Deserialize(InputResponse.ElicitResultJsonTypeInfo)?.Action;
            var sampleText = responses["sample"].Deserialize(InputResponse.CreateMessageResultJsonTypeInfo)?
                .Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            var rootUris = string.Join(",",
                responses["roots"].Deserialize(InputResponse.ListRootsResultJsonTypeInfo)?.Roots.Select(r => r.Uri) ?? []);
            return $"all-ok:elicit={elicitAction},sample={sampleText},roots={rootUris}";
        }

        throw new InputRequiredException(
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
        Assert.SkipWhen(UseStreamableHttp && !Stateless, July2026StatefulStreamableHttpSkipReason);

        var messageTracker = ConfigureServer(MrtrConcurrentThree);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        var elicitCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var samplingCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rootsCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var client = await ConnectAsync(configureClient: options =>
        {
            options.ProtocolVersion = "2026-07-28";
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
        Assert.Equal("2026-07-28", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("mrtr-concurrent-three",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("all-ok:elicit=accept,sample=AI-summary,roots=file:///workspace", text);
        Assert.True(result.IsError is not true);
        messageTracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task Mrtr_LoadShedding_RequestStateOnly_CompletesViaMrtr()
    {
        Assert.SkipWhen(UseStreamableHttp && !Stateless, July2026StatefulStreamableHttpSkipReason);

        var messageTracker = ConfigureServer(
            [McpServerTool(Name = "mrtr-loadshed")] (RequestContext<CallToolRequestParams> context) =>
            {
                if (context.Params!.RequestState is { } state)
                {
                    return $"resumed:{state}";
                }

                // requestState-only InputRequiredException (no inputRequests)
                throw new InputRequiredException(requestState: "deferred-work");
            });
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectExperimentalAsync();
        Assert.Equal("2026-07-28", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("mrtr-loadshed",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("resumed:deferred-work", text);
        Assert.True(result.IsError is not true);
        messageTracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task Mrtr_Backcompat_Roots_ResolvedViaLegacyJsonRpc()
    {
        Assert.SkipWhen(Stateless, "Backcompat requires stateful server for legacy JSON-RPC.");
        var messageTracker = ConfigureServer(
            [McpServerTool(Name = "mrtr-roots-backcompat")] (RequestContext<CallToolRequestParams> context) =>
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
            });
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectLegacyAsync();
        Assert.Equal("2025-11-25", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("mrtr-roots-backcompat",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("roots-ok:Project", text);
        Assert.True(result.IsError is not true);
        messageTracker.AssertMrtrNotUsed();
    }

    [Fact]
    public async Task Mrtr_Backcompat_MultipleInputRequests_ResolvedViaLegacyJsonRpc()
    {
        Assert.SkipWhen(Stateless, "Backcompat requires stateful server for legacy JSON-RPC.");
        var messageTracker = ConfigureServer(
            [McpServerTool(Name = "mrtr-multi-input")] (RequestContext<CallToolRequestParams> context) =>
            {
                if (context.Params!.InputResponses is { } responses &&
                    responses.TryGetValue("confirm", out var elicitResponse) &&
                    responses.TryGetValue("summarize", out var sampleResponse))
                {
                    var action = elicitResponse.Deserialize(InputResponse.ElicitResultJsonTypeInfo)?.Action;
                    var text = sampleResponse.Deserialize(InputResponse.CreateMessageResultJsonTypeInfo)?.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
                    return $"both:{action}:{text}";
                }

                throw new InputRequiredException(
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
        await using var client = await ConnectLegacyAsync();
        Assert.Equal("2025-11-25", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("mrtr-multi-input",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("both:accept:LLM:Summarize", text);
        Assert.True(result.IsError is not true);
        messageTracker.AssertMrtrNotUsed();
    }

    [Fact]
    public async Task Mrtr_Backcompat_AlwaysIncomplete_FailsAfterMaxRetries()
    {
        Assert.SkipWhen(Stateless, "Backcompat requires stateful server for legacy JSON-RPC.");
        int elicitCallCount = 0;

        ConfigureServer(
            [McpServerTool(Name = "mrtr-always-incomplete")] (RequestContext<CallToolRequestParams> context) =>
            {
                // Always throw - never complete
                throw new InputRequiredException(
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
            options.ProtocolVersion = "2025-11-25";
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
        ConfigureServer(
            [McpServerTool(Name = "mrtr-empty-inputs")] (RequestContext<CallToolRequestParams> context) =>
            {
                throw new InputRequiredException(
                    inputRequests: new Dictionary<string, InputRequest>(),
                    requestState: "empty");
            });
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectLegacyAsync();
        Assert.Equal("2025-11-25", client.NegotiatedProtocolVersion);

        var ex = await Assert.ThrowsAsync<McpProtocolException>(() =>
            client.CallToolAsync("mrtr-empty-inputs",
                cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("without input requests", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(McpErrorCode.InternalError, ex.ErrorCode);
    }

    [Fact]
    public async Task Mrtr_Backcompat_ClientHandlerThrows_PropagatesError()
    {
        Assert.SkipWhen(Stateless, "Backcompat requires stateful server for legacy JSON-RPC.");

        ConfigureServer(MrtrElicit);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectAsync(configureClient: options =>
        {
            ConfigureMrtrHandlers(options);
            options.ProtocolVersion = "2025-11-25";
            options.Handlers.ElicitationHandler = (request, ct) =>
            {
                throw new InvalidOperationException("Client-side elicitation failure");
            };
        });
        Assert.Equal("2025-11-25", client.NegotiatedProtocolVersion);

        // Handler exception propagates through the backcompat JSON-RPC round-trip.
        // The original exception message gets wrapped in "Request failed (remote)" during backcompat.
        var ex = await Assert.ThrowsAsync<McpProtocolException>(() =>
            client.CallToolAsync("mrtr-elicit",
                cancellationToken: TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(McpErrorCode.InternalError, ex.ErrorCode);
    }
}
