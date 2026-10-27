using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using System.Collections.Concurrent;
using System.Net;

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

        await using (var setupClient = await TestMcpClient.CreateAsync(setupTransport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken))
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

        await using var client = await TestMcpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

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

        await using var client = await TestMcpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

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

        await using var client = await TestMcpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

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

        await using (var setupClient = await TestMcpClient.CreateAsync(setupTransport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken))
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

        await using var client = await TestMcpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(authDelegateCalledAgain, "AuthorizationCallbackHandler should not be called when refresh token is valid");
        Assert.True(TestOAuthServer.HasRefreshedToken, "Token should have been refreshed");
        Assert.NotEqual("invalid-token", tokenCache.LastStoredToken.AccessToken);
    }

    [Theory]
    [InlineData("", McpServerUrl, false)]
    [InlineData("/mcp", McpServerUrl, false)]
    [InlineData("", McpServerUrl + "/", false)]
    [InlineData("/mcp", McpServerUrl + "/mcp/", false)]
    [InlineData("/mcp", McpServerUrl, true)]
    [InlineData("/mcp", null, false)]
    [InlineData("/mcp", null, true)]
    public async Task GetTokenAsync_ExpiredAccessTokenRefreshesWithCanonicalResource(
        string endpointPath, string? resource, bool coldStart)
    {
        TestOAuthServer.ExpectResource = resource is not null;
        TestOAuthServer.ValidResources = resource is not null ? [resource] : [];

        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.ResourceMetadata!.Resource = resource;
        });
        Builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.TokenValidationParameters.ValidateAudience = resource is not null;
            options.TokenValidationParameters.ValidAudience = resource is not null ? new Uri(resource).ToString() : null;
        });

        if (resource is null)
        {
            Builder.Services.Configure<AuthenticationOptions>(options =>
            {
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            });
        }

        await using var app = await StartMcpServerAsync(endpointPath);
        var handler = new TokenRequestTrackingHandler(legacy: resource is null) { InnerHandler = SocketsHttpHandler };
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        ConfigureHttpClient(httpClient);

        var tokenCache = new TestTokenCache();
        var authorizationCallbackCount = 0;

        HttpClientTransport CreateTransport() => new(new()
        {
            Endpoint = new(McpServerUrl + endpointPath),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = (context, cancellationToken) =>
                {
                    authorizationCallbackCount++;
                    var query = QueryHelpers.ParseQuery(context.AuthorizationUri.Query);
                    Assert.Equal(resource, query.TryGetValue("resource", out var value) ? value.ToString() : null);
                    return HandleAuthorizationUrlAsync(context, cancellationToken);
                },
                TokenCache = tokenCache,
            },
        }, httpClient, LoggerFactory);

        await using var setupTransport = CreateTransport();
        await using var setupClient = await TestMcpClient.CreateAsync(
            setupTransport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(tokenCache.LastStoredToken);
        if (coldStart)
        {
            // A new provider must discover and retain the canonical resource even when its first
            // authorization flow succeeds by refreshing the persisted tokens.
            tokenCache.LastStoredToken.AccessToken = "invalid-token";
        }

        await using var restartedTransport = coldStart ? CreateTransport() : null;
        await using var restartedClient = restartedTransport is null ? null : await TestMcpClient.CreateAsync(
            restartedTransport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
        var client = restartedClient ?? setupClient;

        var expiredTokens = tokenCache.LastStoredToken;
        Assert.NotNull(expiredTokens);
        expiredTokens.ExpiresIn = 0;
        expiredTokens.ObtainedAt = DateTimeOffset.MinValue;
        var challengeCount = handler.UnauthorizedResponseCount;

        await client.PingAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(handler.TokenRequests, request => Assert.Equal(resource, request.Resource));
        Assert.Equal(challengeCount, handler.UnauthorizedResponseCount);
        Assert.True(TestOAuthServer.HasRefreshedToken);
        Assert.NotSame(expiredTokens, tokenCache.LastStoredToken);
        Assert.Equal(1, authorizationCallbackCount);
        Assert.Equal(1, TestOAuthServer.AuthorizationRequestCount);
        Assert.Equal(1, TestOAuthServer.AuthorizationCodeTokenRequestCount);
        Assert.Equal(coldStart ? 3 : 2, TestOAuthServer.TokenRequestCount);
        Assert.Equal(coldStart ? 2 : 1, handler.TokenRequests.Count(request => request.GrantType == "refresh_token"));
    }

    [Fact]
    public async Task GetTokenAsync_AuthorizationServerChangeUpdatesCanonicalResourceForSilentRefresh()
    {
        var metadata = new ProtectedResourceMetadata
        {
            Resource = McpServerUrl,
            AuthorizationServers = [OAuthServerUrl],
            ScopesSupported = ["mcp:tools"],
        };
        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.ResourceMetadata = metadata;
        });
        Builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.TokenValidationParameters.ValidAudience = new Uri(McpServerUrl).ToString();
        });
        // Re-registration changes the test user's subject, which cannot reuse a stateful MCP session.
        Builder.Services.AddMcpServer(options => options.ToolCollection = new())
            .WithHttpTransport(options => options.Stateless = true);

        await using var app = await StartMcpServerAsync("/mcp");
        var handler = new TokenRequestTrackingHandler(legacy: false) { InnerHandler = SocketsHttpHandler };
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        ConfigureHttpClient(httpClient);

        var tokenCache = new TestTokenCache();
        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl + "/mcp"),
            OAuth = new()
            {
                RedirectUri = new("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
                TokenCache = tokenCache,
            },
        }, httpClient, LoggerFactory);

        await using var client = await TestMcpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        const string migratedResource = McpServerUrl + "/";
        const string migratedIssuer = OAuthServerUrl + "/tenant1";
        metadata.Resource = migratedResource;
        metadata.AuthorizationServers = [migratedIssuer];
        TestOAuthServer.ValidResources = [migratedResource];

        Assert.NotNull(tokenCache.LastStoredToken);
        var originalClientId = tokenCache.LastStoredToken.ClientId;
        tokenCache.LastStoredToken.AccessToken = "invalid-token";

        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var expiredTokens = tokenCache.LastStoredToken;
        Assert.NotNull(expiredTokens);
        Assert.NotEqual(originalClientId, expiredTokens.ClientId);
        Assert.Equal(migratedIssuer, expiredTokens.AuthorizationServer);
        expiredTokens.ExpiresIn = 0;
        expiredTokens.ObtainedAt = DateTimeOffset.MinValue;
        var challengeCount = handler.UnauthorizedResponseCount;

        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(challengeCount, handler.UnauthorizedResponseCount);
        Assert.True(TestOAuthServer.HasRefreshedToken);
        Assert.NotSame(expiredTokens, tokenCache.LastStoredToken);
        Assert.Equal(2, TestOAuthServer.AuthorizationRequestCount);
        Assert.Equal(2, TestOAuthServer.AuthorizationCodeTokenRequestCount);
        Assert.Collection(handler.TokenRequests,
            request => Assert.Equal(("authorization_code", McpServerUrl), request),
            request => Assert.Equal(("authorization_code", migratedResource), request),
            request => Assert.Equal(("refresh_token", migratedResource), request));
    }

    [Fact]
    public async Task GetTokenAsync_ExplicitClientDoesNotRefreshTokenFromDifferentAuthorizationServer()
    {
        await using var app = await StartMcpServerAsync();

        var tokenCache = new TestTokenCache();
        await using var setupTransport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
                TokenCache = tokenCache,
            },
        }, HttpClient, LoggerFactory);

        await using (var setupClient = await TestMcpClient.CreateAsync(
            setupTransport,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        Assert.NotNull(tokenCache.LastStoredToken);
        tokenCache.LastStoredToken.AccessToken = "invalid-token";
        tokenCache.LastStoredToken.AuthorizationServer = "https://different-authorization-server.example.com";
        var authorizationCallbackCalled = false;

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
                    authorizationCallbackCalled = true;
                    return HandleAuthorizationUrlAsync(context, ct);
                },
                TokenCache = tokenCache,
            },
        }, HttpClient, LoggerFactory);

        var exception = await Assert.ThrowsAsync<McpException>(() => TestMcpClient.CreateAsync(
            transport,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("explicitly configured client credentials", exception.Message);
        Assert.False(authorizationCallbackCalled);
        Assert.False(TestOAuthServer.HasRefreshedToken);
        Assert.Equal("https://different-authorization-server.example.com", tokenCache.LastStoredToken.AuthorizationServer);
    }

    [Fact]
    public async Task GetTokenAsync_ExplicitClientDoesNotRefreshTokenIssuedToDifferentClient()
    {
        await using var app = await StartMcpServerAsync();

        var tokenCache = new TestTokenCache();
        await using var setupTransport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
                TokenCache = tokenCache,
            },
        }, HttpClient, LoggerFactory);

        await using (var setupClient = await TestMcpClient.CreateAsync(
            setupTransport,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        Assert.NotNull(tokenCache.LastStoredToken);
        tokenCache.LastStoredToken.AccessToken = "invalid-token";
        tokenCache.LastStoredToken.ClientId = "different-client";
        var authorizationCallbackCalled = false;

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
                    authorizationCallbackCalled = true;
                    return HandleAuthorizationUrlAsync(context, ct);
                },
                TokenCache = tokenCache,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await TestMcpClient.CreateAsync(
            transport,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(authorizationCallbackCalled);
        Assert.False(TestOAuthServer.HasRefreshedToken);
        Assert.Equal("demo-client", tokenCache.LastStoredToken.ClientId);
        Assert.Equal(OAuthServerUrl, tokenCache.LastStoredToken.AuthorizationServer);
    }

    [Fact]
    public async Task GetTokenAsync_ExplicitClientWithLegacyUnboundCache_Reauthorizes()
    {
        await using var app = await StartMcpServerAsync();

        var tokenCache = new TestTokenCache();
        await using var setupTransport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
                TokenCache = tokenCache,
            },
        }, HttpClient, LoggerFactory);

        await using (var setupClient = await TestMcpClient.CreateAsync(
            setupTransport,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        Assert.NotNull(tokenCache.LastStoredToken);
        tokenCache.LastStoredToken.AccessToken = "invalid-token";
        tokenCache.LastStoredToken.AuthorizationServer = null;
        var authorizationCallbackCalled = false;

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
                    authorizationCallbackCalled = true;
                    return HandleAuthorizationUrlAsync(context, ct);
                },
                TokenCache = tokenCache,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await TestMcpClient.CreateAsync(
            transport,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(authorizationCallbackCalled);
        Assert.False(TestOAuthServer.HasRefreshedToken);
        Assert.Equal(OAuthServerUrl, tokenCache.LastStoredToken.AuthorizationServer);
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

        await using (var setupClient = await TestMcpClient.CreateAsync(setupTransport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken))
        {
            // Just connecting should trigger dynamic registration, authorization, and storage.
        }

        Assert.True(authDelegateCalledInitially, "Authorization callback should be called for the initial authorization");
        Assert.False(TestOAuthServer.HasRefreshedToken, "Token should not have been refreshed yet");
        Assert.NotNull(tokenCache.LastStoredToken);
        Assert.False(
            string.IsNullOrEmpty(tokenCache.LastStoredToken.ClientId),
            "The dynamically registered client ID should be persisted alongside the tokens");
        Assert.Equal(OAuthServerUrl, tokenCache.LastStoredToken.AuthorizationServer);

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

        await using var client = await TestMcpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(authDelegateCalledAgain, "Authorization callback should not be called when the persisted refresh token can be used");
        Assert.True(TestOAuthServer.HasRefreshedToken, "Token should have been refreshed using the persisted client credentials");
        Assert.NotEqual("invalid-token", tokenCache.LastStoredToken.AccessToken);
    }

    [Fact]
    public async Task GetTokenAsync_ColdStartWithCredentialsFromDifferentAuthorizationServer_Reregisters()
    {
        await using var app = await StartMcpServerAsync();

        var tokenCache = new TestTokenCache();
        await using var setupTransport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                RedirectUri = new Uri("http://localhost:1179/callback"),
                DynamicClientRegistration = new() { ClientName = "Test MCP Client" },
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
                TokenCache = tokenCache,
            },
        }, HttpClient, LoggerFactory);

        await using (var setupClient = await TestMcpClient.CreateAsync(
            setupTransport,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        Assert.NotNull(tokenCache.LastStoredToken);
        tokenCache.LastStoredToken.AccessToken = "invalid-token";
        tokenCache.LastStoredToken.AuthorizationServer = "https://different-authorization-server.example.com";
        var authorizationCallbackCalled = false;

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                RedirectUri = new Uri("http://localhost:1179/callback"),
                DynamicClientRegistration = new() { ClientName = "Test MCP Client" },
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    authorizationCallbackCalled = true;
                    return HandleAuthorizationUrlAsync(context, ct);
                },
                TokenCache = tokenCache,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await TestMcpClient.CreateAsync(
            transport,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(authorizationCallbackCalled);
        Assert.False(TestOAuthServer.HasRefreshedToken);
        Assert.Equal(OAuthServerUrl, tokenCache.LastStoredToken.AuthorizationServer);
    }

    [Fact]
    public async Task GetTokenAsync_ColdStartWithLegacyUnboundCredentials_Reregisters()
    {
        await using var app = await StartMcpServerAsync();

        var tokenCache = new TestTokenCache();
        await using var setupTransport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                RedirectUri = new Uri("http://localhost:1179/callback"),
                DynamicClientRegistration = new() { ClientName = "Test MCP Client" },
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
                TokenCache = tokenCache,
            },
        }, HttpClient, LoggerFactory);

        await using (var setupClient = await TestMcpClient.CreateAsync(
            setupTransport,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        Assert.NotNull(tokenCache.LastStoredToken);
        Assert.False(string.IsNullOrEmpty(tokenCache.LastStoredToken.ClientId));
        tokenCache.LastStoredToken.AccessToken = "invalid-token";
        tokenCache.LastStoredToken.AuthorizationServer = null;
        var authorizationCallbackCalled = false;

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                RedirectUri = new Uri("http://localhost:1179/callback"),
                DynamicClientRegistration = new() { ClientName = "Test MCP Client" },
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    authorizationCallbackCalled = true;
                    return HandleAuthorizationUrlAsync(context, ct);
                },
                TokenCache = tokenCache,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await TestMcpClient.CreateAsync(
            transport,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(authorizationCallbackCalled);
        Assert.False(TestOAuthServer.HasRefreshedToken);
        Assert.Equal(OAuthServerUrl, tokenCache.LastStoredToken.AuthorizationServer);
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

        await using (var setupClient = await TestMcpClient.CreateAsync(setupTransport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken))
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
        await using var client = await TestMcpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

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

    private sealed class TokenRequestTrackingHandler(bool legacy) : DelegatingHandler
    {
        private int _unauthorizedResponseCount;

        public ConcurrentQueue<(string GrantType, string? Resource)> TokenRequests { get; } = new();
        public int UnauthorizedResponseCount => Volatile.Read(ref _unauthorizedResponseCount);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestUri = request.RequestUri!;
            var isMcpServer = requestUri.GetLeftPart(UriPartial.Authority) == McpServerUrl;

            if (legacy && isMcpServer)
            {
                if (requestUri.AbsolutePath.StartsWith("/.well-known/oauth-protected-resource", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                // Legacy discovery uses the MCP origin as the issuer. Serve the test authorization
                // server's metadata there without requiring a second OAuth endpoint proxy.
                if (requestUri.AbsolutePath is "/.well-known/oauth-authorization-server" or "/.well-known/openid-configuration")
                {
                    request.RequestUri = new Uri(OAuthServerUrl + requestUri.PathAndQuery);
                }
            }

            if (requestUri.AbsolutePath == "/token")
            {
                var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
                TokenRequests.Enqueue((
                    form["grant_type"].ToString(),
                    form.TryGetValue("resource", out var resource) ? resource.ToString() : null));
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (isMcpServer && response.StatusCode == HttpStatusCode.Unauthorized)
            {
                Interlocked.Increment(ref _unauthorizedResponseCount);
            }

            return response;
        }
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
