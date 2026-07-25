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
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    authDelegateCalledInitially = true;
                    return HandleAuthorizationUrlAsync(context, ct);
                },
                TokenCache = tokenCache,
            },
        }, HttpClient, LoggerFactory);

        await using (var setupClient = await McpClient.CreateAsync(setupTransport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken))
        {
            // Just connecting should trigger auth and storage.
        }

        Assert.True(authDelegateCalledInitially, "AuthorizationCallbackHandler should be called to get initial token");
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
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    authDelegateCalledAgain = true;
                    return HandleAuthorizationUrlAsync(context, ct);
                },
                TokenCache = tokenCache
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(authDelegateCalledAgain, "AuthorizationCallbackHandler should not be called when token is valid");
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
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
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
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    authDelegateCalled = true;
                    return HandleAuthorizationUrlAsync(context, ct);
                },
                TokenCache = tokenCache,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(authDelegateCalled, "AuthorizationCallbackHandler should be called when cached token is invalid");
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
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    authDelegateCalledInitially = true;
                    return HandleAuthorizationUrlAsync(context, ct);
                },
                TokenCache = tokenCache,
            },
        }, HttpClient, LoggerFactory);

        await using (var setupClient = await McpClient.CreateAsync(setupTransport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken))
        {
            // Just connecting should trigger auth and storage.
        }

        Assert.True(authDelegateCalledInitially, "AuthorizationCallbackHandler should be called to get initial token");
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
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    authDelegateCalledAgain = true;
                    return HandleAuthorizationUrlAsync(context, ct);
                },
                TokenCache = tokenCache
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(authDelegateCalledAgain, "AuthorizationCallbackHandler should not be called when refresh token is valid");
        Assert.True(TestOAuthServer.HasRefreshedToken, "Token should have been refreshed");
        Assert.NotEqual("invalid-token", tokenCache.LastStoredToken.AccessToken);
    }

    [Fact]
    public async Task GetTokenAsync_ColdStartWithDynamicRegistration_RefreshesUsingPersistedCredentials()
    {
        await using var app = await StartMcpServerAsync();

        var tokenCache = new TestTokenCache();
        var authDelegateCalledInitially = false;

        // First "process": no ClientId is configured, so the client registers dynamically.
        await using var setupTransport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                RedirectUri = new Uri("http://localhost:1179/callback"),
                DynamicClientRegistration = new()
                {
                    ClientName = "Test MCP Client",
                    ClientUri = new Uri("https://example.com"),
                },
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    authDelegateCalledInitially = true;
                    return HandleAuthorizationUrlAsync(context, ct);
                },
                TokenCache = tokenCache,
            },
        }, HttpClient, LoggerFactory);

        await using (var setupClient = await McpClient.CreateAsync(setupTransport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken))
        {
            // Just connecting should trigger dynamic registration, authorization, and storage.
        }

        Assert.True(authDelegateCalledInitially, "Authorization callback should be called for the initial authorization");
        Assert.False(TestOAuthServer.HasRefreshedToken, "Token should not have been refreshed yet");
        Assert.NotNull(tokenCache.LastStoredToken);
        Assert.False(
            string.IsNullOrEmpty(tokenCache.LastStoredToken.ClientId),
            "The dynamically registered client ID should be persisted alongside the tokens");

        // Simulate a cold start: the access token is no longer valid, but the refresh token persists.
        // The new provider has no client ID configured and must restore it from the cache to refresh
        // instead of throwing "Client ID is not available".
        tokenCache.LastStoredToken.AccessToken = "invalid-token";
        var authDelegateCalledAgain = false;

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                RedirectUri = new Uri("http://localhost:1179/callback"),
                DynamicClientRegistration = new()
                {
                    ClientName = "Test MCP Client",
                    ClientUri = new Uri("https://example.com"),
                },
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    authDelegateCalledAgain = true;
                    return HandleAuthorizationUrlAsync(context, ct);
                },
                TokenCache = tokenCache,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(authDelegateCalledAgain, "Authorization callback should not be called when the persisted refresh token can be used");
        Assert.True(TestOAuthServer.HasRefreshedToken, "Token should have been refreshed using the persisted client credentials");
        Assert.NotEqual("invalid-token", tokenCache.LastStoredToken.AccessToken);
    }

    [Fact]
    public async Task GetTokenAsync_ColdStartWithoutPersistedClientId_FallsBackToReauthorization()
    {
        await using var app = await StartMcpServerAsync();

        var tokenCache = new TestTokenCache();
        var authDelegateCalledInitially = false;

        await using var setupTransport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                RedirectUri = new Uri("http://localhost:1179/callback"),
                DynamicClientRegistration = new()
                {
                    ClientName = "Test MCP Client",
                    ClientUri = new Uri("https://example.com"),
                },
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    authDelegateCalledInitially = true;
                    return HandleAuthorizationUrlAsync(context, ct);
                },
                TokenCache = tokenCache,
            },
        }, HttpClient, LoggerFactory);

        await using (var setupClient = await McpClient.CreateAsync(setupTransport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        Assert.True(authDelegateCalledInitially, "Authorization callback should be called for the initial authorization");
        Assert.NotNull(tokenCache.LastStoredToken);

        // Simulate a durable cache that persisted tokens but not the client registration (for example
        // an entry written by an older version). On a cold start the provider cannot refresh without a
        // client ID and must fall back to a fresh authorization rather than throwing.
        tokenCache.LastStoredToken.AccessToken = "invalid-token";
        tokenCache.LastStoredToken.ClientId = null;
        tokenCache.LastStoredToken.ClientSecret = null;
        tokenCache.LastStoredToken.TokenEndpointAuthMethod = null;
        var authDelegateCalledAgain = false;

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                RedirectUri = new Uri("http://localhost:1179/callback"),
                DynamicClientRegistration = new()
                {
                    ClientName = "Test MCP Client",
                    ClientUri = new Uri("https://example.com"),
                },
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    authDelegateCalledAgain = true;
                    return HandleAuthorizationUrlAsync(context, ct);
                },
                TokenCache = tokenCache,
            },
        }, HttpClient, LoggerFactory);

        // Should not throw "Client ID is not available"; instead re-authorizes via dynamic
        // registration and the authorization-code flow.
        await using var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(authDelegateCalledAgain, "Authorization callback should be called to re-authorize when no client ID is available to refresh");
        Assert.False(TestOAuthServer.HasRefreshedToken, "A refresh should not be attempted without a client ID");
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
