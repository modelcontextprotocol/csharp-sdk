using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using System.Collections.Concurrent;

namespace ModelContextProtocol.AspNetCore.Tests.OAuth;

public class DiscoveryTimeoutTests(ITestOutputHelper outputHelper) : OAuthTestBase(outputHelper)
{
    private static readonly TimeSpan ProbeBudget = TimeSpan.FromMilliseconds(500);
    private readonly ConcurrentQueue<string> _methods = new();
    private readonly AsyncGate _authorization = new();
    private int _callbackCount;

    [Fact]
    public async Task SlowSilentAcquisition_IsExcludedBeforeTheInitialPost()
    {
        ConfigureModernServer();
        await using var app = await StartMcpServerAsync();
        var cache = new GatedCache();
        await using var transport = CreateTransport(cache);
        _authorization.Release.SetResult();
        var connecting = McpClient.CreateAsync(transport, Options(), LoggerFactory, TestContext.Current.CancellationToken);
        await cache.Gate.AssertStillWaitingAsync(ProbeBudget * 2);
        Assert.Empty(_methods);
        cache.Gate.Release.SetResult();

        await using var client = await connecting.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        Assert.Equal(McpProtocolVersions.July2026ProtocolVersion, client.NegotiatedProtocolVersion);
        Assert.Equal([RequestMethods.ServerDiscover], _methods);
        Assert.Equal(1, _callbackCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Authorization_ObservesCallerAndInitializationCancellation(bool initializationTimeout)
    {
        ConfigureModernServer();
        await using var app = await StartMcpServerAsync();
        await using var transport = CreateTransport();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var options = Options();
        options.InitializationTimeout = initializationTimeout ? ProbeBudget * 4 : TestConstants.DefaultTimeout;
        var connecting = McpClient.CreateAsync(transport, options, LoggerFactory, caller.Token);
        await _authorization.Entered.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        if (initializationTimeout)
        {
            var error = await Assert.ThrowsAsync<TimeoutException>(() => connecting.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken));
            Assert.Equal("Initialization timed out", error.Message);
        }
        else
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting);
        }
        await _authorization.Canceled.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        Assert.Equal(1, _callbackCount);
        Assert.Empty(_methods);
    }

    [Fact]
    public async Task SlowAuthorization_PreservesModernProtocol_WithoutSuspendingAnotherClientsProbe()
    {
        ConfigureModernServer();
        int posts = 0;
        var secondPost = new AsyncGate();
        await using var app = await StartMcpServerAsync(configureMiddleware: app =>
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Method == HttpMethods.Post && Interlocked.Increment(ref posts) == 2)
                {
                    await secondPost.WaitAsync(context.RequestAborted);
                }
                await next();
            });
            app.UseAuthentication();
            app.UseAuthorization();
        });
        await using var transport = CreateTransport();
        var first = McpClient.CreateAsync(transport, Options(), LoggerFactory, TestContext.Current.CancellationToken);
        await _authorization.Entered.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        var secondOptions = Options(pinned: true);
        secondOptions.DiscoverProbeTimeout = ProbeBudget * 2;
        var second = McpClient.CreateAsync(transport, secondOptions, LoggerFactory, TestContext.Current.CancellationToken);
        await secondPost.Entered.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        // The second probe expiring proves the first authorization survived more than its own budget.
        await Assert.ThrowsAsync<McpException>(() => second.WaitAsync(ProbeBudget * 8, TestContext.Current.CancellationToken));
        Assert.False(_authorization.Canceled.Task.IsCompleted);
        _authorization.Release.SetResult();
        await using var client = await first.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        Assert.Equal(McpProtocolVersions.July2026ProtocolVersion, client.NegotiatedProtocolVersion);
        Assert.Equal(1, _callbackCount);
        Assert.Equal([RequestMethods.ServerDiscover], _methods);
    }

    [Fact]
    public async Task Authentication_RestartsProbeBudgetBeforeRetryHeaders()
    {
        ConfigureModernServer();
        var initialHeaders = new AsyncGate();
        var retryHeaders = new AsyncGate();
        await using var app = await StartMcpServerAsync(configureMiddleware: app =>
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Method == HttpMethods.Post)
                {
                    await (context.Request.Headers.Authorization.Count == 0 ? initialHeaders : retryHeaders).WaitAsync(context.RequestAborted);
                }
                await next();
            });
            app.UseAuthentication();
            app.UseAuthorization();
        });
        await using var transport = CreateTransport();
        var options = Options(pinned: true);
        options.DiscoverProbeTimeout = TimeSpan.FromSeconds(2);
        var connecting = McpClient.CreateAsync(transport, options, LoggerFactory, TestContext.Current.CancellationToken);
        await initialHeaders.AssertStillWaitingAsync(options.DiscoverProbeTimeout * 0.6);
        initialHeaders.Release.SetResult();
        await _authorization.Entered.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        _authorization.Release.SetResult();
        await retryHeaders.AssertStillWaitingAsync(options.DiscoverProbeTimeout * 0.6);
        retryHeaders.Release.SetResult();

        await using var client = await connecting.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        Assert.Equal(McpProtocolVersions.July2026ProtocolVersion, client.NegotiatedProtocolVersion);
        Assert.Equal(1, _callbackCount);
        Assert.Equal([RequestMethods.ServerDiscover], _methods);
    }

    [Fact]
    public async Task AuthenticatedRetryHeaders_RemainProbeBounded()
    {
        ConfigureModernServer();
        var headers = new AsyncGate();
        await using var app = await StartMcpServerAsync(configureMiddleware: app => app.Use(async (context, next) =>
        {
            if (context.Request.Method == HttpMethods.Post && context.Request.Headers.Authorization.Count > 0)
            {
                await headers.WaitAsync(context.RequestAborted);
            }
            await next();
        }));
        await using var transport = CreateTransport();
        _authorization.Release.SetResult();
        var connecting = McpClient.CreateAsync(transport, Options(pinned: true), LoggerFactory, TestContext.Current.CancellationToken);
        await headers.Entered.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        await headers.Canceled.Task.WaitAsync(ProbeBudget * 8, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<McpException>(() => connecting);
        Assert.Equal(1, _callbackCount);
    }

    [Fact]
    public async Task AuthenticatedDiscoveryBodyTimeout_AbortsModernHandlerWithoutCancellationRpc()
    {
        var handler = new AsyncGate();
        ConfigureModernServer();
        Builder.Services.AddHttpContextAccessor();
        Builder.Services.AddMcpServer().WithMessageFilters(filters => filters.AddIncomingFilter(next => async (context, cancellationToken) =>
        {
            if (context.JsonRpcMessage is JsonRpcRequest { Method: RequestMethods.ServerDiscover })
            {
                var httpContext = context.Services!.GetRequiredService<IHttpContextAccessor>().HttpContext!;
                httpContext.Response.ContentType = "text/event-stream";
                await httpContext.Response.WriteAsync(": waiting\n\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(cancellationToken);
                await handler.WaitAsync(cancellationToken);
            }
            await next(context, cancellationToken);
        }));
        await using var app = await StartMcpServerAsync();
        await using var transport = CreateTransport();
        _authorization.Release.SetResult();
        var connecting = McpClient.CreateAsync(transport, Options(pinned: true), LoggerFactory, TestContext.Current.CancellationToken);
        await handler.Entered.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        await handler.Canceled.Task.WaitAsync(ProbeBudget * 4, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<McpException>(() => connecting);
        Assert.Equal(1, _callbackCount);
        Assert.Equal([RequestMethods.ServerDiscover], _methods);
    }

    private void ConfigureModernServer()
    {
        Builder.Services.AddMcpServer().WithHttpTransport(options => options.Stateless = true)
            .WithMessageFilters(filters => filters.AddIncomingFilter(next => async (context, cancellationToken) =>
            {
                if (context.JsonRpcMessage is JsonRpcRequest request)
                {
                    _methods.Enqueue(request.Method);
                }
                else if (context.JsonRpcMessage is JsonRpcNotification notification)
                {
                    _methods.Enqueue(notification.Method);
                }
                await next(context, cancellationToken);
            }));
    }

    private HttpClientTransport CreateTransport(ITokenCache? cache = null) => new(new()
    {
        Endpoint = new(McpServerUrl),
        TransportMode = HttpTransportMode.StreamableHttp,
        OAuth = new()
        {
            ClientId = "demo-client",
            ClientSecret = "demo-secret",
            RedirectUri = new("http://localhost:1179/callback"),
            TokenCache = cache,
            AuthorizationCallbackHandler = async (context, cancellationToken) =>
            {
                Interlocked.Increment(ref _callbackCount);
                await _authorization.WaitAsync(cancellationToken);
                return await HandleAuthorizationUrlAsync(context, cancellationToken);
            },
        },
    }, HttpClient, LoggerFactory);

    private static McpClientOptions Options(bool pinned = false) => new()
    {
        DiscoverProbeTimeout = ProbeBudget,
        ProtocolVersion = pinned ? McpProtocolVersions.July2026ProtocolVersion : null,
    };

    private sealed class GatedCache : ITokenCache
    {
        private TokenContainer? _tokens;
        public AsyncGate Gate { get; } = new();
        public async ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
        {
            await Gate.WaitAsync(cancellationToken);
            return _tokens;
        }
        public ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
        {
            _tokens = tokens;
            return default;
        }
    }
}
