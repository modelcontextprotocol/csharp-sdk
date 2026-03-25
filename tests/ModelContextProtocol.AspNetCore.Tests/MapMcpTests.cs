using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text.Json;

namespace ModelContextProtocol.AspNetCore.Tests;

public abstract class MapMcpTests(ITestOutputHelper testOutputHelper) : KestrelInMemoryTest(testOutputHelper)
{
    protected abstract bool UseStreamableHttp { get; }
    protected abstract bool Stateless { get; }

    protected void ConfigureStateless(HttpServerTransportOptions options)
    {
        options.Stateless = Stateless;
    }

    private ServerMessageTracker ConfigureExperimentalServer(params Delegate[] tools)
    {
        var messageTracker = new ServerMessageTracker();
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = "MapMcpTests", Version = "1" };
            options.ExperimentalProtocolVersion = "2026-06-XX";
            messageTracker.AddFilters(options.Filters.Message);
        })
        .WithHttpTransport(ConfigureStateless)
        .WithTools(tools.Select(t => McpServerTool.Create(t)));
        return messageTracker;
    }

    private Task<McpClient> ConnectExperimentalAsync()
    {
        var options = CreateDefaultClientOptions();
        options.ExperimentalProtocolVersion = "2026-06-XX";
        return ConnectAsync(clientOptions: options);
    }

    private Task<McpClient> ConnectDefaultAsync() => ConnectAsync(clientOptions: CreateDefaultClientOptions());

    private static McpClientOptions CreateDefaultClientOptions()
    {
        var options = new McpClientOptions();
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
        return options;
    }


    protected async Task<McpClient> ConnectAsync(
        string? path = null,
        HttpClientTransportOptions? transportOptions = null,
        McpClientOptions? clientOptions = null)
    {
        // Default behavior when no options are provided
        path ??= UseStreamableHttp ? "/" : "/sse";

        await using var transport = new HttpClientTransport(transportOptions ?? new HttpClientTransportOptions
        {
            Endpoint = new Uri($"http://localhost:5000{path}"),
            TransportMode = UseStreamableHttp ? HttpTransportMode.StreamableHttp : HttpTransportMode.Sse,
        }, HttpClient, LoggerFactory);

        return await McpClient.CreateAsync(transport, clientOptions, LoggerFactory, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MapMcp_ThrowsInvalidOperationException_IfWithHttpTransportIsNotCalled()
    {
        Builder.Services.AddMcpServer();
        await using var app = Builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(() => app.MapMcp());
        Assert.StartsWith("You must call WithHttpTransport()", exception.Message);
    }

    [Fact]
    public async Task Can_UseIHttpContextAccessor_InTool()
    {
        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless).WithTools<EchoHttpContextUserTools>();

        Builder.Services.AddHttpContextAccessor();

        await using var app = Builder.Build();

        app.Use(next =>
        {
            return async context =>
            {
                context.User = CreateUser("TestUser");
                await next(context);
            };
        });

        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync();

        var response = await mcpClient.CallToolAsync(
            "echo_with_user_name",
            new Dictionary<string, object?>() { ["message"] = "Hello world!" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(response.Content.OfType<TextContentBlock>());
        Assert.Equal("TestUser: Hello world!", content.Text);
    }

    [Fact]
    public async Task Messages_FromNewUser_AreRejected()
    {
        Assert.SkipWhen(Stateless, "User validation across requests is not applicable in stateless mode.");

        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless).WithTools<EchoHttpContextUserTools>();

        // Add an authentication scheme that will send a 403 Forbidden response.
        Builder.Services.AddAuthentication().AddBearerToken();
        Builder.Services.AddHttpContextAccessor();

        await using var app = Builder.Build();

        app.Use(next =>
        {
            var i = 0;
            return async context =>
            {
                context.User = CreateUser($"TestUser{Interlocked.Increment(ref i)}");
                await next(context);
            };
        });

        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        var httpRequestException = await Assert.ThrowsAsync<HttpRequestException>(() => ConnectAsync());
        Assert.Equal(HttpStatusCode.Forbidden, httpRequestException.StatusCode);
    }

    [Fact]
    public async Task ClaimsPrincipal_CanBeInjected_IntoToolMethod()
    {
        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless).WithTools<ClaimsPrincipalTools>();

        await using var app = Builder.Build();

        app.Use(next => async context =>
        {
            context.User = CreateUser("TestUser");
            await next(context);
        });

        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var client = await ConnectAsync();

        var response = await client.CallToolAsync(
            "echo_claims_principal",
            new Dictionary<string, object?>() { ["message"] = "Hello world!" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(response.Content.OfType<TextContentBlock>());
        Assert.Equal("TestUser: Hello world!", content.Text);
    }

    [Fact]
    public async Task Sampling_DoesNotCloseStreamPrematurely()
    {
        Assert.SkipWhen(Stateless, "Sampling is not supported in stateless mode.");

        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless).WithTools<SamplingRegressionTools>();

        await using var app = Builder.Build();

        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        var sampleCount = 0;
        var clientOptions = new McpClientOptions()
        {
            Handlers = new()
            {
                SamplingHandler = async (parameters, _, _) =>
                {
                    Assert.NotNull(parameters?.Messages);
                    var message = Assert.Single(parameters.Messages);
                    Assert.Equal(Role.User, message.Role);
                    Assert.Equal("Test prompt for sampling", Assert.IsType<TextContentBlock>(Assert.Single(message.Content)).Text);

                    sampleCount++;
                    return new CreateMessageResult
                    {
                        Model = "test-model",
                        Role = Role.Assistant,
                        Content = [new TextContentBlock { Text = "Sampling response from client" }],
                    };
                }
            }
        };

        await using var mcpClient = await ConnectAsync(clientOptions: clientOptions);

        var result = await mcpClient.CallToolAsync("sampling-tool", new Dictionary<string, object?>
        {
            ["prompt"] = "Test prompt for sampling"
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Null(result.IsError);
        var textContent = Assert.Single(result.Content);
        Assert.Equal("text", textContent.Type);
        Assert.Equal("Sampling completed successfully. Client responded: Sampling response from client", Assert.IsType<TextContentBlock>(textContent).Text);

        Assert.Equal(2, sampleCount);

        // Verify that the tool call and the sampling request both used the same ID to ensure we cover against regressions.
        // https://github.com/modelcontextprotocol/csharp-sdk/issues/464
        Assert.Single(MockLoggerProvider.LogMessages, m =>
            m.Category == "ModelContextProtocol.Client.McpClient" &&
            m.Message.Contains("request '2' for method 'tools/call'"));

        Assert.Single(MockLoggerProvider.LogMessages, m =>
            m.Category == "ModelContextProtocol.Server.McpServer" &&
            m.Message.Contains("request '2' for method 'sampling/createMessage'"));
    }

    [Fact]
    public async Task Server_ShutsDownQuickly_WhenClientIsConnected()
    {
        Builder.Services.AddMcpServer().WithHttpTransport().WithTools<ClaimsPrincipalTools>();

        await using var app = Builder.Build();
        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        // Connect a client which will open a long-running GET request (SSE or Streamable HTTP)
        await using var mcpClient = await ConnectAsync();

        // Verify the client is connected
        var tools = await mcpClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(tools);

        // Now measure how long it takes to stop the server
        var stopwatch = Stopwatch.StartNew();
        await app.StopAsync(TestContext.Current.CancellationToken);
        stopwatch.Stop();

        // The server should shut down quickly (within a few seconds). We use 5 seconds as a generous threshold.
        // This is much less than the default HostOptions.ShutdownTimeout of 30 seconds.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Server took {stopwatch.Elapsed.TotalSeconds:F2} seconds to shut down with a connected client. " +
            "This suggests the GET request is not respecting ApplicationStopping token.");
    }

    [Fact]
    public async Task LongRunningToolCall_DoesNotTimeout_WhenNoEventStreamStore()
    {
        // Regression test for: Tool calls that last over HttpClient timeout without producing
        // intermediate notifications will timeout because HttpClient doesn't see the 200 response
        // until the first message is written. When primingItem is null (no ISseEventStreamStore),
        // we should flush the response stream so HttpClient sees the 200 immediately.

        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless).WithTools<LongRunningTools>();

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        // Retry a couple of times to reduce occasional flakiness on low-resource machines.
        // If the server regresses to flushing only after tool completion, each attempt should still fail
        // because HttpClient timeout (1 second) is below the tool duration (2 seconds).
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                // Create a custom HttpClient with a very short timeout (1 second)
                // The tool will take 2 seconds to complete
                using var shortTimeoutClient = new HttpClient(SocketsHttpHandler, disposeHandler: false)
                {
                    BaseAddress = new Uri("http://localhost:5000/"),
                    Timeout = TimeSpan.FromSeconds(1)
                };

                var path = UseStreamableHttp ? "/" : "/sse";
                var transportMode = UseStreamableHttp ? HttpTransportMode.StreamableHttp : HttpTransportMode.Sse;

                await using var transport = new HttpClientTransport(new()
                {
                    Endpoint = new($"http://localhost:5000{path}"),
                    TransportMode = transportMode,
                }, shortTimeoutClient, LoggerFactory);

                await using var mcpClient = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

                // Call a tool that takes 2 seconds - this should succeed despite the 1 second HttpClient timeout
                // because the response stream is flushed immediately after receiving the request
                var response = await mcpClient.CallToolAsync(
                    "long_running_operation",
                    new Dictionary<string, object?>() { ["durationMs"] = 2000 },
                    cancellationToken: TestContext.Current.CancellationToken);

                var content = Assert.Single(response.Content.OfType<TextContentBlock>());
                Assert.Equal("Operation completed after 2000ms", content.Text);
                return;
            }
            catch (OperationCanceledException) when (attempt < 2)
            {
                // Retry intermittent timeout-related failures on slow CI machines.
            }
        }

    }

    // =====================================================================
    // MRTR tests: experimental (native), backcompat (legacy JSON-RPC), and edge cases.
    // Each test creates its own server with ExperimentalProtocolVersion enabled.
    // =====================================================================

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
    public async Task Mrtr_Experimental_Elicitation_CompletesViaMrtr()
    {
        var messageTracker = ConfigureExperimentalServer(MrtrElicit);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectExperimentalAsync();

        var result = await client.CallToolAsync("mrtr-elicit",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("elicit-ok:accept", text);
        messageTracker.AssertMrtrUsed();
    }

    [McpServerTool(Name = "mrtr-sample")]
    private static string MrtrSample(RequestContext<CallToolRequestParams> context)
    {
        if (context.Params!.InputResponses is { } responses &&
            responses.TryGetValue("llm_call", out var response))
        {
            var text = response.SamplingResult?.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
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
    }

    [Fact]
    public async Task Mrtr_Experimental_Sampling_CompletesViaMrtr()
    {
        var messageTracker = ConfigureExperimentalServer(MrtrSample);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectExperimentalAsync();

        var result = await client.CallToolAsync("mrtr-sample",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("sample-ok:LLM:Summarize this", text);
        messageTracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task Mrtr_Experimental_Roots_CompletesViaMrtr()
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

    [Fact]
    public async Task Mrtr_Experimental_MultiRoundTrip_CompletesAcrossRetries()
    {
        var messageTracker = ConfigureExperimentalServer(MrtrMulti);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectExperimentalAsync();

        var result = await client.CallToolAsync("mrtr-multi",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("multi-done:greeting=accept", text);
        messageTracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task Mrtr_Experimental_IsMrtrSupported_ReturnsTrue()
    {
        ConfigureExperimentalServer([McpServerTool(Name = "mrtr-check")] (McpServer server) => server.IsMrtrSupported.ToString());
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectExperimentalAsync();

        var result = await client.CallToolAsync("mrtr-check",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("True", text);
    }

    [McpServerTool(Name = "mrtr-lowlevel")]
    private static string MrtrLowLevel(McpServer server, RequestContext<CallToolRequestParams> context)
    {
        if (context.Params!.RequestState is not null && context.Params!.InputResponses is { } responses)
        {
            var result = responses["user_confirm"].ElicitationResult;
            return $"lowlevel-confirmed:{result?.Action}:{context.Params.RequestState}";
        }

        if (!server.IsMrtrSupported)
        {
            return "lowlevel-unsupported";
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
    }

    [Fact]
    public async Task Mrtr_Experimental_LowLevel_IncompleteResultException_WorksEndToEnd()
    {
        var messageTracker = ConfigureExperimentalServer(MrtrLowLevel);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectExperimentalAsync();

        var result = await client.CallToolAsync("mrtr-lowlevel",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.StartsWith("lowlevel-confirmed:accept:", text);
        messageTracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task Mrtr_Backcompat_Elicitation_ResolvedViaLegacyJsonRpc()
    {
        Assert.SkipWhen(Stateless, "Backcompat requires stateful server for legacy JSON-RPC.");
        var messageTracker = ConfigureExperimentalServer(MrtrElicit);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectDefaultAsync();
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("mrtr-elicit",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("elicit-ok:accept", text);
        messageTracker.AssertMrtrNotUsed();
    }

    [Fact]
    public async Task Mrtr_Backcompat_Sampling_ResolvedViaLegacyJsonRpc()
    {
        Assert.SkipWhen(Stateless, "Backcompat requires stateful server for legacy JSON-RPC.");
        var messageTracker = ConfigureExperimentalServer(MrtrSample);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectDefaultAsync();
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("mrtr-sample",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("sample-ok:LLM:Summarize this", text);
        messageTracker.AssertMrtrNotUsed();
    }

    [Fact]
    public async Task Mrtr_Backcompat_MultiRoundTrip_ResolvedViaLegacyJsonRpc()
    {
        Assert.SkipWhen(Stateless, "Backcompat requires stateful server for legacy JSON-RPC.");
        var messageTracker = ConfigureExperimentalServer(MrtrMulti);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectDefaultAsync();
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("mrtr-multi",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("multi-done:greeting=accept", text);
        messageTracker.AssertMrtrNotUsed();
    }

    [Fact]
    public async Task Mrtr_Backcompat_IsMrtrSupported_ReturnsTrue()
    {
        Assert.SkipWhen(Stateless, "Backcompat requires stateful server for legacy JSON-RPC.");
        ConfigureExperimentalServer([McpServerTool(Name = "mrtr-check")] (McpServer server) => server.IsMrtrSupported.ToString());
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectDefaultAsync();
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("mrtr-check",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("True", text);
    }

    [Fact]
    public async Task Mrtr_Backcompat_LowLevel_ResolvedViaLegacyJsonRpc()
    {
        Assert.SkipWhen(Stateless, "Backcompat requires stateful server for legacy JSON-RPC.");
        var messageTracker = ConfigureExperimentalServer(MrtrLowLevel);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectDefaultAsync();
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("mrtr-lowlevel",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.StartsWith("lowlevel-confirmed:accept:", text);
        messageTracker.AssertMrtrNotUsed();
    }

    [Fact]
    public async Task Mrtr_Experimental_HighLevel_Elicitation_CompletesViaMrtr()
    {
        Assert.SkipWhen(Stateless, "High-level API requires stateful handler suspension.");
        var messageTracker = ConfigureExperimentalServer(
            [McpServerTool(Name = "mrtr-hl-elicit")] async (string message, McpServer server, CancellationToken ct) =>
            {
                var result = await server.ElicitAsync(new ElicitRequestParams
                {
                    Message = message,
                    RequestedSchema = new()
                }, ct);
                return $"{result.Action}:{result.Content?.FirstOrDefault().Value}";
            });
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectExperimentalAsync();

        var result = await client.CallToolAsync("mrtr-hl-elicit",
            new Dictionary<string, object?> { ["message"] = "Please confirm" },
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("accept:yes", text);
        messageTracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task Mrtr_Experimental_HighLevel_Sampling_CompletesViaMrtr()
    {
        Assert.SkipWhen(Stateless, "High-level API requires stateful handler suspension.");
        var messageTracker = ConfigureExperimentalServer(
            [McpServerTool(Name = "mrtr-hl-sample")] async (string prompt, McpServer server, CancellationToken ct) =>
            {
                var result = await server.SampleAsync(new CreateMessageRequestParams
                {
                    Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = prompt }] }],
                    MaxTokens = 100
                }, ct);
                return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "No response";
            });
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectExperimentalAsync();

        var result = await client.CallToolAsync("mrtr-hl-sample",
            new Dictionary<string, object?> { ["prompt"] = "Hello" },
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("LLM:Hello", text);
        messageTracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task Mrtr_Experimental_HighLevel_Roots_CompletesViaMrtr()
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

    [Fact]
    public async Task Mrtr_Experimental_HighLevel_MultiRoundTrip_CompletesViaMrtr()
    {
        Assert.SkipWhen(Stateless, "High-level API requires stateful handler suspension.");
        var messageTracker = ConfigureExperimentalServer(
            [McpServerTool(Name = "mrtr-hl-multi-elicit")] async (McpServer server, CancellationToken ct) =>
            {
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
                return $"{greetingResult.Content?.FirstOrDefault().Value} {nameResult.Content?.FirstOrDefault().Value}!";
            });
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectExperimentalAsync();

        var result = await client.CallToolAsync("mrtr-hl-multi-elicit",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("Hello Alice!", text);
        messageTracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task Mrtr_Backcompat_HighLevel_Elicitation_ResolvedViaLegacyJsonRpc()
    {
        Assert.SkipWhen(Stateless, "Backcompat requires stateful server for legacy JSON-RPC.");
        var messageTracker = ConfigureExperimentalServer(
            [McpServerTool(Name = "mrtr-hl-elicit")] async (string message, McpServer server, CancellationToken ct) =>
            {
                var result = await server.ElicitAsync(new ElicitRequestParams
                {
                    Message = message,
                    RequestedSchema = new()
                }, ct);
                return $"{result.Action}:{result.Content?.FirstOrDefault().Value}";
            });
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectDefaultAsync();
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("mrtr-hl-elicit",
            new Dictionary<string, object?> { ["message"] = "Please confirm" },
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("accept:yes", text);
        messageTracker.AssertMrtrNotUsed();
    }

    [Fact]
    public async Task Mrtr_Backcompat_HighLevel_Sampling_ResolvedViaLegacyJsonRpc()
    {
        Assert.SkipWhen(Stateless, "Backcompat requires stateful server for legacy JSON-RPC.");
        var messageTracker = ConfigureExperimentalServer(
            [McpServerTool(Name = "mrtr-hl-sample")] async (string prompt, McpServer server, CancellationToken ct) =>
            {
                var result = await server.SampleAsync(new CreateMessageRequestParams
                {
                    Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = prompt }] }],
                    MaxTokens = 100
                }, ct);
                return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "No response";
            });
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectDefaultAsync();
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("mrtr-hl-sample",
            new Dictionary<string, object?> { ["prompt"] = "Hello" },
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("LLM:Hello", text);
        messageTracker.AssertMrtrNotUsed();
    }

    [Fact]
    public async Task Mrtr_Backcompat_HighLevel_MultiRoundTrip_ResolvedViaLegacyJsonRpc()
    {
        Assert.SkipWhen(Stateless, "Backcompat requires stateful server for legacy JSON-RPC.");
        var messageTracker = ConfigureExperimentalServer(
            [McpServerTool(Name = "mrtr-hl-multi-elicit")] async (McpServer server, CancellationToken ct) =>
            {
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
                return $"{greetingResult.Content?.FirstOrDefault().Value} {nameResult.Content?.FirstOrDefault().Value}!";
            });
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectDefaultAsync();
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var result = await client.CallToolAsync("mrtr-hl-multi-elicit",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("Hello Alice!", text);
        messageTracker.AssertMrtrNotUsed();
    }

    [Fact]
    public async Task Mrtr_IsMrtrSupported_ReturnsFalse_WhenClientDoesNotOptIn_InStatelessMode()
    {
        Assert.SkipUnless(Stateless, "This test verifies the stateless-specific false case.");
        ConfigureExperimentalServer([McpServerTool(Name = "mrtr-check")] (McpServer server) => server.IsMrtrSupported.ToString());
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectDefaultAsync();

        var result = await client.CallToolAsync("mrtr-check",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("False", text);
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

        var options = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
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

        await using var client = await ConnectAsync(clientOptions: options);

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
            [McpServerTool(Name = "mrtr-loadshed")] (McpServer server, RequestContext<CallToolRequestParams> context) =>
            {
                if (context.Params!.RequestState is { } state)
                {
                    return $"resumed:{state}";
                }

                if (!server.IsMrtrSupported)
                {
                    return "MRTR not supported.";
                }

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
    public async Task Mrtr_Stateless_LowLevel_ReturnsFallback_WhenMrtrNotSupported()
    {
        Assert.SkipUnless(Stateless, "In stateful mode, IsMrtrSupported=true via backcompat.");
        ConfigureExperimentalServer(MrtrLowLevel);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectDefaultAsync();

        var result = await client.CallToolAsync("mrtr-lowlevel",
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("lowlevel-unsupported", text);
    }

    [Fact]
    public async Task Mrtr_Stateless_IncompleteResultException_WithoutMrtrClient_ThrowsError()
    {
        Assert.SkipUnless(Stateless, "In stateful mode, backcompat resolves this.");
        ConfigureExperimentalServer(MrtrElicit);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectDefaultAsync();

        var ex = await Assert.ThrowsAsync<McpProtocolException>(() =>
            client.CallToolAsync("mrtr-elicit",
                cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("stateless", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MRTR", ex.Message);
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
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

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
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

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
        var options = CreateDefaultClientOptions();
        var originalHandler = options.Handlers.ElicitationHandler!;
        options.Handlers.ElicitationHandler = (request, ct) =>
        {
            Interlocked.Increment(ref elicitCallCount);
            return originalHandler(request, ct);
        };

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
        await using var client = await ConnectAsync(clientOptions: options);
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

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
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        var ex = await Assert.ThrowsAsync<McpProtocolException>(() =>
            client.CallToolAsync("mrtr-empty-inputs",
                cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("without input requests", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mrtr_Backcompat_ClientHandlerThrows_PropagatesError()
    {
        Assert.SkipWhen(Stateless, "Backcompat requires stateful server for legacy JSON-RPC.");
        var options = CreateDefaultClientOptions();
        options.Handlers.ElicitationHandler = (request, ct) =>
        {
            throw new InvalidOperationException("Client-side elicitation failure");
        };

        ConfigureExperimentalServer(MrtrElicit);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectAsync(clientOptions: options);
        Assert.NotEqual("2026-06-XX", client.NegotiatedProtocolVersion);

        // Handler exception propagates through the backcompat JSON-RPC round-trip
        await Assert.ThrowsAsync<McpProtocolException>(() =>
            client.CallToolAsync("mrtr-elicit",
                cancellationToken: TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Mrtr_Experimental_MixedHighAndLowLevel_WorksInSameSession()
    {
        Assert.SkipWhen(Stateless, "High-level API requires stateful handler suspension.");
        var messageTracker = ConfigureExperimentalServer(
            [McpServerTool(Name = "mrtr-hl-sample-mixed")] async (string prompt, McpServer server, CancellationToken ct) =>
            {
                var result = await server.SampleAsync(new CreateMessageRequestParams
                {
                    Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = prompt }] }],
                    MaxTokens = 100
                }, ct);
                return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "No response";
            },
            MrtrElicit);
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var client = await ConnectExperimentalAsync();

        // Call the high-level sampling tool
        var samplingResult = await client.CallToolAsync("mrtr-hl-sample-mixed",
            new Dictionary<string, object?> { ["prompt"] = "test prompt" },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("LLM:test prompt", Assert.IsType<TextContentBlock>(Assert.Single(samplingResult.Content)).Text);

        // Call the low-level elicitation tool in the same session
        var elicitResult = await client.CallToolAsync("mrtr-elicit",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("elicit-ok:accept", Assert.IsType<TextContentBlock>(Assert.Single(elicitResult.Content)).Text);

        messageTracker.AssertMrtrUsed();
    }

    private ClaimsPrincipal CreateUser(string name)
        => new(new ClaimsIdentity(
            [new Claim("name", name), new Claim(ClaimTypes.NameIdentifier, name)],
            "TestAuthType", "name", "role"));

    [McpServerToolType]
    protected class EchoHttpContextUserTools(IHttpContextAccessor contextAccessor)
    {
        [McpServerTool, Description("Echoes the input back to the client with their user name.")]
        public string EchoWithUserName(string message)
        {
            var httpContext = contextAccessor.HttpContext ?? throw new Exception("HttpContext unavailable!");
            var userName = httpContext.User.Identity?.Name ?? "anonymous";
            return $"{userName}: {message}";
        }
    }

    [McpServerToolType]
    protected class ClaimsPrincipalTools
    {
        [McpServerTool, Description("Echoes the input back to the client with the user name from ClaimsPrincipal.")]
        public string EchoClaimsPrincipal(ClaimsPrincipal? user, string message)
        {
            var userName = user?.Identity?.Name ?? "anonymous";
            return $"{userName}: {message}";
        }
    }

    [McpServerToolType]
    private class SamplingRegressionTools
    {
        [McpServerTool(Name = "sampling-tool")]
        public static async Task<string> SamplingToolAsync(McpServer server, string prompt, CancellationToken cancellationToken)
        {
            // This tool reproduces the scenario described in https://github.com/modelcontextprotocol/csharp-sdk/issues/464
            // 1. The client calls tool with request ID 2, because it's the first request after the initialize request.
            // 2. This tool makes two sampling requests which use IDs 1 and 2.
            // 3. In the old buggy Streamable HTTP transport code, this would close the SSE response stream,
            //    because the second sampling request used an ID matching the tool call.
            var samplingRequest = new CreateMessageRequestParams
            {
                Messages = [
                    new SamplingMessage
                    {
                        Role = Role.User,
                        Content = [new TextContentBlock { Text = prompt }],
                    }
                ],
                MaxTokens = 1000
            };

            await server.SampleAsync(samplingRequest, cancellationToken);
            var samplingResult = await server.SampleAsync(samplingRequest, cancellationToken);

            return $"Sampling completed successfully. Client responded: {Assert.IsType<TextContentBlock>(Assert.Single(samplingResult.Content)).Text}";
        }
    }

    [McpServerToolType]
    protected class LongRunningTools
    {
        [McpServerTool, Description("Simulates a long-running operation")]
        public static async Task<string> LongRunningOperation(
            [Description("Duration of the operation in milliseconds")] int durationMs,
            CancellationToken cancellationToken)
        {
            await Task.Delay(durationMs, cancellationToken);
            return $"Operation completed after {durationMs}ms";
        }
    }

}
