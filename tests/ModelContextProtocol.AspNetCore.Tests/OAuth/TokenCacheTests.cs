using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace ModelContextProtocol.AspNetCore.Tests.OAuth;

public class TokenCacheTests : OAuthTestBase
{
    public TokenCacheTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    [Fact]
    public async Task GetTokenAsync_CachedAccessTokenIsUsedForOutgoingRequests()
    {
        await using var app = await StartMcpServerAsync();

        var tokenCache = new TestTokenCache();
        bool authDelegateCalledInitially = false;

        await using var setupTransport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationRedirectDelegate = (uri, redirect, ct) =>
                {
                    authDelegateCalledInitially = true;
                    return HandleAuthorizationUrlAsync(uri, redirect, ct);
                },
                TokenCache = tokenCache,
            },
        }, HttpClient, LoggerFactory);

        await using (var setupClient = await McpClient.CreateAsync(setupTransport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken))
        {
            // Just connecting should trigger auth and storage.
        }

        Assert.True(authDelegateCalledInitially, "AuthorizationRedirectDelegate should be called to get initial token");
        Assert.NotNull(tokenCache.LastStoredToken);

        var authDelegateCalledAgain = false;

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationRedirectDelegate = (uri, redirect, ct) =>
                {
                    authDelegateCalledAgain = true;
                    return HandleAuthorizationUrlAsync(uri, redirect, ct);
                },
                TokenCache = tokenCache
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(authDelegateCalledAgain, "AuthorizationRedirectDelegate should not be called when token is valid");
    }

    [Fact]
    public async Task StoreTokenAsync_NewlyAcquiredAccessTokenIsCached()
    {
        await using var app = await StartMcpServerAsync();

        var tokenCache = new TestTokenCache();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
                TokenCache = tokenCache
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(tokenCache.LastStoredToken);
        Assert.False(string.IsNullOrEmpty(tokenCache.LastStoredToken.AccessToken));
    }

    [Fact]
    public async Task GetTokenAsync_InvalidCachedTokenTriggersAuthDelegate()
    {
        await using var app = await StartMcpServerAsync();

        var tokenCache = new TestTokenCache(CreateInvalidToken());
        bool authDelegateCalled = false;

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationRedirectDelegate = (uri, redirect, ct) =>
                {
                    authDelegateCalled = true;
                    return HandleAuthorizationUrlAsync(uri, redirect, ct);
                },
                TokenCache = tokenCache,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(authDelegateCalled, "AuthorizationRedirectDelegate should be called when cached token is invalid");
        Assert.NotNull(tokenCache.LastStoredToken);
        Assert.NotEqual("invalid-token", tokenCache.LastStoredToken.AccessToken);
    }

    [Fact]
    public async Task GetTokenAsync_InvalidAccessTokenTriggersRefresh()
    {
        await using var app = await StartMcpServerAsync();

        var tokenCache = new TestTokenCache();
        bool authDelegateCalledInitially = false;

        await using var setupTransport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationRedirectDelegate = (uri, redirect, ct) =>
                {
                    authDelegateCalledInitially = true;
                    return HandleAuthorizationUrlAsync(uri, redirect, ct);
                },
                TokenCache = tokenCache,
            },
        }, HttpClient, LoggerFactory);

        await using (var setupClient = await McpClient.CreateAsync(setupTransport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken))
        {
            // Just connecting should trigger auth and storage.
        }

        Assert.True(authDelegateCalledInitially, "AuthorizationRedirectDelegate should be called to get initial token");
        Assert.False(TestOAuthServer.HasRefreshedToken, "Token should not have been refreshed yet");
        Assert.NotNull(tokenCache.LastStoredToken);

        // Invalidate the access token but keep the refresh token valid (if any)
        tokenCache.LastStoredToken.AccessToken = "invalid-token";
        var authDelegateCalledAgain = false;

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationRedirectDelegate = (uri, redirect, ct) =>
                {
                    authDelegateCalledAgain = true;
                    return HandleAuthorizationUrlAsync(uri, redirect, ct);
                },
                TokenCache = tokenCache
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(authDelegateCalledAgain, "AuthorizationRedirectDelegate should not be called when refresh token is valid");
        Assert.True(TestOAuthServer.HasRefreshedToken, "Token should have been refreshed");
        Assert.NotEqual("invalid-token", tokenCache.LastStoredToken.AccessToken);
    }

    private TokenContainer CreateInvalidToken()
    {
        return new TokenContainer
        {
            TokenType = "Bearer",
            AccessToken = "invalid-token",
            ObtainedAt = DateTimeOffset.UtcNow,
        };
    }

    private class TestTokenCache(TokenContainer? initialToken = null) : ITokenCache
    {
        public TokenContainer? LastStoredToken { get; private set; } = initialToken;

        public ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
        {
            return new ValueTask<TokenContainer?>(LastStoredToken);
        }

        public ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
        {
            LastStoredToken = tokens;
            return ValueTask.CompletedTask;
        }
    }
}
