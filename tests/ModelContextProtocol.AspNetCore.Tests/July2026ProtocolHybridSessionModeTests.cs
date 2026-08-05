using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// End-to-end coverage for <see cref="HttpServerSessionMode.StatefulForInitializeClients"/>: a single endpoint
/// that serves <c>initialize</c>-handshake clients with full stateful sessions while serving <c>2026-07-28</c>
/// and later clients statelessly, without forcing them to downgrade
/// (<see href="https://github.com/modelcontextprotocol/csharp-sdk/issues/1777"/>).
/// </summary>
[McpServerToolType]
public class July2026ProtocolHybridSessionModeTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{
    private WebApplication? _app;
    private int _configureSessionOptionsCount;
    private int _runSessionHandlerCount;

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
        base.Dispose();
    }

    [McpServerTool(Name = "greet")]
    public static string Greet([System.ComponentModel.Description("Name to greet")] string name) => $"Hello, {name}!";

    [McpServerTool(Name = "greet_via_elicit")]
    public static async Task<string> GreetViaElicit(McpServer server, CancellationToken cancellationToken)
    {
        // Server→client requests only work over a stateful session, so this proves the initialize-handshake
        // half of a hybrid endpoint keeps its session even though the endpoint also serves stateless requests.
        var elicitResult = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = "What is your name?",
            RequestedSchema = new(),
        }, cancellationToken);

        var name = elicitResult.Content?.TryGetValue("answer", out var answer) == true
            ? answer.GetString()
            : "stranger";

        return $"Hello, {name}!";
    }

    [McpServerTool(Name = "scope_state")]
    public static string ScopeState(ScopedService scopedService) => scopedService.State ?? "<not the http request scope>";

    private async Task StartHybridServerAsync(bool trackRunSessionHandler = false)
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = nameof(July2026ProtocolHybridSessionModeTests), Version = "1" };
        })
            .WithHttpTransport(options =>
            {
                options.SessionMode = HttpServerSessionMode.StatefulForInitializeClients;
                options.ConfigureSessionOptions = (httpContext, mcpServerOptions, cancellationToken) =>
                {
                    Interlocked.Increment(ref _configureSessionOptionsCount);
                    return Task.CompletedTask;
                };

                if (trackRunSessionHandler)
                {
#pragma warning disable MCPEXP002 // RunSessionHandler is experimental.
                    options.RunSessionHandler = async (httpContext, server, cancellationToken) =>
                    {
                        Interlocked.Increment(ref _runSessionHandlerCount);
                        await server.RunAsync(cancellationToken);
                    };
#pragma warning restore MCPEXP002
                }
            })
            .WithTools<July2026ProtocolHybridSessionModeTests>();

        Builder.Services.AddScoped<ScopedService>();

        _app = Builder.Build();

        _app.Use(next => context =>
        {
            context.RequestServices.GetRequiredService<ScopedService>().State = "From request middleware!";
            return next(context);
        });

        _app.MapMcp();
        await _app.StartAsync(TestContext.Current.CancellationToken);
    }

    private Task<McpClient> ConnectClientAsync(string? protocolVersion = null, Action<McpClientOptions>? configureClient = null)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost:5000/"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, HttpClient, LoggerFactory);

        // A null ProtocolVersion prefers 2026-07-28 and probes with server/discover before considering a
        // fallback to the initialize handshake. Pinning an older version forces the initialize handshake.
        var clientOptions = new McpClientOptions { ProtocolVersion = protocolVersion };
        configureClient?.Invoke(clientOptions);
        return McpClient.CreateAsync(transport, clientOptions, LoggerFactory, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ModernAndLegacyClients_ShareOneEndpoint_AndModernDoesNotDowngrade()
    {
        await StartHybridServerAsync();

        await using var modernClient = await ConnectClientAsync();
        await using var legacyClient = await ConnectClientAsync(McpProtocolVersions.November2025ProtocolVersion);

        // The whole point of the hybrid mode: the default client keeps 2026-07-28 instead of downgrading.
        Assert.Equal(McpProtocolVersions.July2026ProtocolVersion, modernClient.NegotiatedProtocolVersion);
        Assert.Null(modernClient.SessionId);

        Assert.Equal(McpProtocolVersions.November2025ProtocolVersion, legacyClient.NegotiatedProtocolVersion);
        Assert.False(string.IsNullOrEmpty(legacyClient.SessionId));

        // Both halves of the endpoint remain usable while the other is connected.
        var modernResult = await modernClient.CallToolAsync("greet",
            new Dictionary<string, object?> { ["name"] = "Modern" },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Hello, Modern!", Assert.IsType<TextContentBlock>(Assert.Single(modernResult.Content)).Text);

        var legacyResult = await legacyClient.CallToolAsync("greet",
            new Dictionary<string, object?> { ["name"] = "Legacy" },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Hello, Legacy!", Assert.IsType<TextContentBlock>(Assert.Single(legacyResult.Content)).Text);
    }

    [Fact]
    public async Task LegacyClient_OnHybridServer_StillSupportsServerToClientElicitation()
    {
        await StartHybridServerAsync();

        await using var legacyClient = await ConnectClientAsync(McpProtocolVersions.November2025ProtocolVersion, options =>
        {
            options.Handlers.ElicitationHandler = (request, ct) => new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["answer"] = JsonDocument.Parse("\"Bob\"").RootElement.Clone(),
                },
            });
        });

        var result = await legacyClient.CallToolAsync("greet_via_elicit",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError is not true);
        Assert.Equal("Hello, Bob!", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task ModernRequests_UseRequestScopedServices_WhileLegacySessionsUseApplicationServices()
    {
        await StartHybridServerAsync();

        await using var modernClient = await ConnectClientAsync();
        await using var legacyClient = await ConnectClientAsync(McpProtocolVersions.November2025ProtocolVersion);

        // Stateless requests resolve services from HttpContext.RequestServices, so the tool observes the state
        // that the ASP.NET Core middleware set on the request-scoped service.
        var modernResult = await modernClient.CallToolAsync("scope_state", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("From request middleware!", Assert.IsType<TextContentBlock>(Assert.Single(modernResult.Content)).Text);

        // Stateful sessions outlive the HTTP request, so they scope requests off the application services
        // instead and never see the middleware's request-scoped state.
        var legacyResult = await legacyClient.CallToolAsync("scope_state", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("<not the http request scope>", Assert.IsType<TextContentBlock>(Assert.Single(legacyResult.Content)).Text);
    }

    [Fact]
    public async Task ConfigureSessionOptions_RunsPerRequestForModernClients_AndOncePerSessionForLegacyClients()
    {
        await StartHybridServerAsync();

        var beforeLegacyConnect = Volatile.Read(ref _configureSessionOptionsCount);
        await using var legacyClient = await ConnectClientAsync(McpProtocolVersions.November2025ProtocolVersion);

        // The initialize request creates the session; notifications/initialized reuses it.
        Assert.Equal(1, Volatile.Read(ref _configureSessionOptionsCount) - beforeLegacyConnect);

        var beforeLegacyCalls = Volatile.Read(ref _configureSessionOptionsCount);
        await legacyClient.CallToolAsync("greet", new Dictionary<string, object?> { ["name"] = "Legacy" }, cancellationToken: TestContext.Current.CancellationToken);
        await legacyClient.CallToolAsync("greet", new Dictionary<string, object?> { ["name"] = "Legacy" }, cancellationToken: TestContext.Current.CancellationToken);

        // Subsequent requests reuse the session, so the callback does not run again.
        Assert.Equal(0, Volatile.Read(ref _configureSessionOptionsCount) - beforeLegacyCalls);

        await using var modernClient = await ConnectClientAsync();

        var beforeModernCall = Volatile.Read(ref _configureSessionOptionsCount);
        await modernClient.CallToolAsync("greet", new Dictionary<string, object?> { ["name"] = "Modern" }, cancellationToken: TestContext.Current.CancellationToken);

        // Each 2026-07-28 POST creates a fresh per-request server, so the callback runs again.
        Assert.Equal(1, Volatile.Read(ref _configureSessionOptionsCount) - beforeModernCall);
    }

    [Fact]
    public async Task RunSessionHandler_RunsPerRequestForModernClients_AndOncePerSessionForLegacyClients()
    {
        await StartHybridServerAsync(trackRunSessionHandler: true);

        var beforeLegacyConnect = Volatile.Read(ref _runSessionHandlerCount);
        await using var legacyClient = await ConnectClientAsync(McpProtocolVersions.November2025ProtocolVersion);
        Assert.Equal(1, Volatile.Read(ref _runSessionHandlerCount) - beforeLegacyConnect);

        var beforeLegacyCalls = Volatile.Read(ref _runSessionHandlerCount);
        await legacyClient.CallToolAsync("greet", new Dictionary<string, object?> { ["name"] = "Legacy" }, cancellationToken: TestContext.Current.CancellationToken);
        await legacyClient.CallToolAsync("greet", new Dictionary<string, object?> { ["name"] = "Legacy" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, Volatile.Read(ref _runSessionHandlerCount) - beforeLegacyCalls);

        var beforeModernConnect = Volatile.Read(ref _runSessionHandlerCount);
        await using var modernClient = await ConnectClientAsync();
        Assert.Equal(1, Volatile.Read(ref _runSessionHandlerCount) - beforeModernConnect);

        var beforeModernCall = Volatile.Read(ref _runSessionHandlerCount);
        await modernClient.CallToolAsync("greet", new Dictionary<string, object?> { ["name"] = "Modern" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, Volatile.Read(ref _runSessionHandlerCount) - beforeModernCall);
    }

    [Fact]
    public async Task ModernPost_DoesNotMintSessionId_WhileLegacyInitializeDoes()
    {
        await StartHybridServerAsync();

        using var modernResponse = await SendAsync(HttpMethod.Post, McpProtocolVersions.July2026ProtocolVersion, DiscoverRequest, mcpMethod: "server/discover");
        Assert.Equal(HttpStatusCode.OK, modernResponse.StatusCode);
        Assert.False(modernResponse.Headers.Contains("Mcp-Session-Id"), "2026-07-28 responses must not include Mcp-Session-Id.");

        using var legacyResponse = await SendAsync(HttpMethod.Post, protocolVersion: null, InitializeRequest);
        Assert.Equal(HttpStatusCode.OK, legacyResponse.StatusCode);
        Assert.False(string.IsNullOrEmpty(Assert.Single(legacyResponse.Headers.GetValues("Mcp-Session-Id"))));
    }

    [Fact]
    public async Task ModernPost_IgnoresMcpSessionIdHeader()
    {
        await StartHybridServerAsync();

        // SEP-2567 removed sessions from the 2026-07-28 revision, so a stray session ID must neither be honored
        // nor looked up against the stateful session manager the hybrid endpoint keeps for legacy clients.
        using var response = await SendAsync(HttpMethod.Post, McpProtocolVersions.July2026ProtocolVersion, DiscoverRequest,
            mcpMethod: "server/discover", sessionId: "non-existent-session-id");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Mcp-Session-Id"));
    }

    [Fact]
    public async Task LegacyGetAndDelete_RemainAvailable_WhileModernGetAndDeleteReturn405()
    {
        await StartHybridServerAsync();

        using var initializeResponse = await SendAsync(HttpMethod.Post, protocolVersion: null, InitializeRequest);
        var sessionId = Assert.Single(initializeResponse.Headers.GetValues("Mcp-Session-Id"));

        // The GET and DELETE endpoints are still mapped, so legacy clients keep the unsolicited-message stream
        // and explicit session termination.
        using var legacyGet = await SendAsync(HttpMethod.Get, McpProtocolVersions.November2025ProtocolVersion, content: null, sessionId: sessionId);
        Assert.Equal(HttpStatusCode.OK, legacyGet.StatusCode);

        using var modernGet = await SendAsync(HttpMethod.Get, McpProtocolVersions.July2026ProtocolVersion, content: null);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, modernGet.StatusCode);
        Assert.Equal(["POST"], modernGet.Content.Headers.Allow);

        using var modernDelete = await SendAsync(HttpMethod.Delete, McpProtocolVersions.July2026ProtocolVersion, content: null);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, modernDelete.StatusCode);
        Assert.Equal(["POST"], modernDelete.Content.Headers.Allow);

        using var legacyDelete = await SendAsync(HttpMethod.Delete, McpProtocolVersions.November2025ProtocolVersion, content: null, sessionId: sessionId);
        Assert.Equal(HttpStatusCode.OK, legacyDelete.StatusCode);

        // The session is gone, which proves the legacy DELETE was honored rather than short-circuited.
        using var afterDelete = await SendAsync(HttpMethod.Post, McpProtocolVersions.November2025ProtocolVersion, ListToolsRequest, sessionId: sessionId);
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    private Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string? protocolVersion,
        string? content = null,
        string? mcpMethod = null,
        string? sessionId = null)
    {
        var request = new HttpRequestMessage(method, "");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (protocolVersion is not null)
        {
            request.Headers.Add("MCP-Protocol-Version", protocolVersion);
        }

        if (mcpMethod is not null)
        {
            request.Headers.Add("Mcp-Method", mcpMethod);
        }

        if (sessionId is not null)
        {
            request.Headers.Add("Mcp-Session-Id", sessionId);
        }

        if (content is not null)
        {
            request.Content = new StringContent(content, Encoding.UTF8, "application/json");
        }

        return HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
    }

    private static string DiscoverRequest => """
        {"jsonrpc":"2.0","id":1,"method":"server/discover","params":{"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"HybridTestClient","version":"1.0"},"io.modelcontextprotocol/clientCapabilities":{}}}}
        """;

    private static string InitializeRequest => """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"HybridTestClient","version":"1.0"}}}
        """;

    private static string ListToolsRequest => """
        {"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
        """;

    public class ScopedService
    {
        public string? State { get; set; }
    }
}
