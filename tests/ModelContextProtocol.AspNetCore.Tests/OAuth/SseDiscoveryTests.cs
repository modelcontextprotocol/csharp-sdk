using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using System.Collections.Concurrent;
using System.Text.Json;

namespace ModelContextProtocol.AspNetCore.Tests.OAuth;

public class SseDiscoveryTests(ITestOutputHelper outputHelper) : OAuthTestBase(outputHelper)
{
    private static readonly TimeSpan ProbeBudget = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(HttpTransportMode.AutoDetect, null)]
    [InlineData(HttpTransportMode.Sse, null)]
    [InlineData(HttpTransportMode.AutoDetect, "2025-11-25")]
    [InlineData(HttpTransportMode.Sse, "2025-11-25")]
    [InlineData(HttpTransportMode.AutoDetect, "2026-07-28")]
    [InlineData(HttpTransportMode.Sse, "2026-07-28")]
    public async Task Sse_DefaultsToInitialize_AndHonorsExplicitTransportAndVersion(HttpTransportMode mode, string? version)
    {
        var methods = new ConcurrentQueue<string>();
        var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ConfigureSse(methods, initialized);
        var timeProvider = new FakeTimeProvider();
        var authorization = new AsyncGate();
        var initialEndpointMethods = new ConcurrentQueue<string>();
        await using var app = await StartMcpServerAsync(configureMiddleware: app => app.Use(async (context, next) =>
        {
            if (context.Request.Method == HttpMethods.Post && context.Request.Path == "/sse")
            {
                context.Request.EnableBuffering();
                var message = await JsonSerializer.DeserializeAsync<JsonRpcMessage>(context.Request.Body, McpJsonUtilities.DefaultOptions, context.RequestAborted);
                initialEndpointMethods.Enqueue(Assert.IsType<JsonRpcRequest>(message).Method);
                context.Request.Body.Position = 0;
            }
            await next();
        }));
        await using var transport = CreateTransport(mode, authorization);
        var connecting = McpClient.CreateAsync(transport, new()
        {
            TimeProvider = timeProvider,
            DiscoverProbeTimeout = ProbeBudget,
            ProtocolVersion = version,
            InitializationTimeout = mode == HttpTransportMode.Sse && version is null ? ProbeBudget : TestConstants.DefaultTimeout,
        }, LoggerFactory, TestContext.Current.CancellationToken);
        if (version is null)
        {
            // AutoDetect excludes GET establishment from the probe; explicit SSE precedes initialization.
            await authorization.WaitUntilEnteredAsync(connecting);
            timeProvider.Advance(ProbeBudget * 2);
            Assert.False(authorization.Token.IsCancellationRequested);
        }
        authorization.Release.SetResult();
        bool modern = version == McpProtocolVersions.July2026ProtocolVersion;
        if (modern && mode == HttpTransportMode.AutoDetect)
        {
            await Assert.ThrowsAsync<McpException>(() => connecting.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken));
            Assert.Empty(methods);
        }
        else
        {
            await using var client = await connecting.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
            Assert.Equal(version ?? McpProtocolVersions.November2025ProtocolVersion, client.NegotiatedProtocolVersion);
            Assert.Empty(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            if (!modern)
            {
                await initialized.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
            }
            Assert.Equal(modern
                ? [RequestMethods.ServerDiscover, RequestMethods.ToolsList]
                : new[] { RequestMethods.Initialize, RequestMethods.ToolsList }, methods);
        }
        Assert.Equal(1, TestOAuthServer.AuthorizationCodeTokenRequestCount);
        Assert.Equal(mode == HttpTransportMode.Sse ? [] :
            new[] { version == McpProtocolVersions.November2025ProtocolVersion ? RequestMethods.Initialize : RequestMethods.ServerDiscover },
            initialEndpointMethods);
    }

    [Fact]
    public async Task ExplicitModernSse_SilentDiscoveryTimesOutWithoutInitialize()
    {
        var methods = new ConcurrentQueue<string>();
        var timeProvider = new FakeTimeProvider();
        var discoveryReceived = new AsyncGate();
        ConfigureSse(methods);
        Builder.Services.AddMcpServer().WithMessageFilters(filters => filters.AddIncomingFilter(next => async (context, cancellationToken) =>
        {
            if (context.JsonRpcMessage is JsonRpcRequest { Method: RequestMethods.ServerDiscover })
            {
                await discoveryReceived.WaitAsync(cancellationToken);
            }
            else
            {
                await next(context, cancellationToken);
            }
        }));
        var authorization = new AsyncGate();
        authorization.Release.SetResult();
        await using var app = await StartMcpServerAsync();
        await using var transport = CreateTransport(HttpTransportMode.Sse, authorization);
        var connecting = McpClient.CreateAsync(transport, new()
        {
            TimeProvider = timeProvider,
            ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion,
            DiscoverProbeTimeout = ProbeBudget,
        }, LoggerFactory, TestContext.Current.CancellationToken);
        await discoveryReceived.WaitUntilEnteredAsync(connecting);
        timeProvider.Advance(ProbeBudget);
        await Assert.ThrowsAsync<McpException>(() => connecting.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken));
        Assert.Contains(RequestMethods.ServerDiscover, methods);
        Assert.DoesNotContain(RequestMethods.Initialize, methods);
    }

    [Theory]
    [InlineData("caller")]
    [InlineData("initialization")]
    [InlineData("connection")]
    public async Task SseGetAuthorization_PreservesExistingDeadlines(string deadline)
    {
        ConfigureSse(new());
        var timeProvider = new FakeTimeProvider();
        var authorization = new AsyncGate();
        await using var app = await StartMcpServerAsync();
        await using var transport = CreateTransport(HttpTransportMode.AutoDetect, authorization,
            deadline == "connection" ? TimeSpan.FromSeconds(2) : null);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var connecting = McpClient.CreateAsync(transport, new()
        {
            TimeProvider = timeProvider,
            DiscoverProbeTimeout = ProbeBudget,
            InitializationTimeout = deadline == "initialization" ? ProbeBudget * 4 : TestConstants.DefaultTimeout,
        }, LoggerFactory, caller.Token);
        if (deadline != "connection")
        {
            await authorization.WaitUntilEnteredAsync(connecting);
        }
        if (deadline == "caller")
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken));
        }
        else if (deadline == "initialization")
        {
            timeProvider.Advance(ProbeBudget * 4);
            var error = await Assert.ThrowsAsync<TimeoutException>(() => connecting.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken));
            Assert.Equal("Initialization timed out", error.Message);
        }
        else
        {
            // ConnectionTimeout uses real time and may expire before authorization starts.
            var error = await Assert.ThrowsAsync<HttpRequestException>(() => connecting.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken));
            Assert.IsType<TimeoutException>(error.InnerException);
        }
        if (authorization.Entered.Task.IsCompleted)
        {
            await authorization.Canceled.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        }
        Assert.Equal(0, TestOAuthServer.AuthorizationCodeTokenRequestCount);
    }

    private void ConfigureSse(ConcurrentQueue<string> methods, TaskCompletionSource? initialized = null)
    {
        TestOAuthServer.ValidResources = [.. TestOAuthServer.ValidResources, $"{McpServerUrl}/sse"];
        Builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
            options => options.TokenValidationParameters.ValidAudiences = [$"{McpServerUrl}/sse"]);
        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme,
            options => options.ResourceMetadata!.Resource = $"{McpServerUrl}/sse");
        Builder.Services.AddMcpServer().WithHttpTransport(options => options.EnableLegacySse = true)
            .WithListToolsHandler((_, _) => ValueTask.FromResult(new ListToolsResult { Tools = [] }))
            .WithMessageFilters(filters => filters.AddIncomingFilter(next => async (context, cancellationToken) =>
            {
                if (context.JsonRpcMessage is JsonRpcRequest request)
                {
                    methods.Enqueue(request.Method);
                }
                else if (context.JsonRpcMessage is JsonRpcNotification { Method: NotificationMethods.InitializedNotification })
                {
                    initialized?.TrySetResult();
                }
                await next(context, cancellationToken);
            }));
    }

    private HttpClientTransport CreateTransport(HttpTransportMode mode, AsyncGate authorization, TimeSpan? connectionTimeout = null)
        => new(new()
        {
            Endpoint = new($"{McpServerUrl}/sse"),
            TransportMode = mode,
            ConnectionTimeout = connectionTimeout ?? TestConstants.DefaultTimeout,
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = async (context, cancellationToken) =>
                {
                    await authorization.WaitAsync(cancellationToken);
                    return await HandleAuthorizationUrlAsync(context, cancellationToken);
                },
            },
        }, HttpClient, LoggerFactory);
}
