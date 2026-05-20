using System.Net;
using System.Text.Json.Nodes;
using ModelContextProtocol.Authentication;

namespace ModelContextProtocol.Tests;

public sealed class CrossApplicationAccessTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HttpClient _httpClient;

    public CrossApplicationAccessTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _mockHandler.Dispose();
    }

    #region RequestJwtAuthorizationGrantAsync Tests

    [Fact]
    public async Task RequestJwtAuthorizationGrantAsync_SuccessfulExchange_ReturnsJag()
    {
        var expectedJag = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.test-jag-payload.signature";
        _mockHandler.Handler = _ => JsonResponse(HttpStatusCode.OK, new JsonObject
        {
            ["access_token"] = expectedJag,
            ["issued_token_type"] = CrossApplicationAccess.TokenTypeIdJag,
            ["token_type"] = "N_A",
            ["expires_in"] = 300,
        });

        var options = new RequestJwtAuthGrantOptions
        {
            TokenEndpoint = "https://idp.example.com/oauth2/token",
            Audience = "https://auth.mcp-server.example.com",
            Resource = "https://mcp-server.example.com",
            IdToken = "test-id-token",
            ClientId = "test-client-id",
            HttpClient = _httpClient,
        };

        var jag = await CrossApplicationAccess.RequestJwtAuthorizationGrantAsync(options, TestContext.Current.CancellationToken);

        Assert.Equal(expectedJag, jag);
    }

    [Fact]
    public async Task RequestJwtAuthorizationGrantAsync_SendsCorrectFormData()
    {
        string? capturedBody = null;
        _mockHandler.AsyncHandler = async request =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.OK, new JsonObject
            {
                ["access_token"] = "test-jag",
                ["issued_token_type"] = CrossApplicationAccess.TokenTypeIdJag,
                ["token_type"] = "N_A",
            });
        };

        var options = new RequestJwtAuthGrantOptions
        {
            TokenEndpoint = "https://idp.example.com/oauth2/token",
            Audience = "https://auth.mcp-server.example.com",
            Resource = "https://mcp-server.example.com",
            IdToken = "my-id-token",
            ClientId = "my-client-id",
            ClientSecret = "my-secret",
            Scope = "openid email",
            HttpClient = _httpClient,
        };

        await CrossApplicationAccess.RequestJwtAuthorizationGrantAsync(options, TestContext.Current.CancellationToken);

        Assert.NotNull(capturedBody);
        var formParams = ParseFormData(capturedBody!);
        Assert.Equal(CrossApplicationAccess.GrantTypeTokenExchange, formParams["grant_type"]);
        Assert.Equal(CrossApplicationAccess.TokenTypeIdJag, formParams["requested_token_type"]);
        Assert.Equal("my-id-token", formParams["subject_token"]);
        Assert.Equal(CrossApplicationAccess.TokenTypeIdToken, formParams["subject_token_type"]);
        Assert.Equal("https://auth.mcp-server.example.com", formParams["audience"]);
        Assert.Equal("https://mcp-server.example.com", formParams["resource"]);
        Assert.Equal("my-client-id", formParams["client_id"]);
        Assert.Equal("my-secret", formParams["client_secret"]);
        Assert.Equal("openid email", formParams["scope"]);
    }

    [Fact]
    public async Task RequestJwtAuthorizationGrantAsync_WithoutOptionalParams_OmitsThem()
    {
        string? capturedBody = null;
        _mockHandler.AsyncHandler = async request =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.OK, new JsonObject
            {
                ["access_token"] = "test-jag",
                ["issued_token_type"] = CrossApplicationAccess.TokenTypeIdJag,
                ["token_type"] = "N_A",
            });
        };

        var options = new RequestJwtAuthGrantOptions
        {
            TokenEndpoint = "https://idp.example.com/token",
            Audience = "https://auth.example.com",
            Resource = "https://resource.example.com",
            IdToken = "test-id-token",
            ClientId = "test-client-id",
            HttpClient = _httpClient,
        };

        await CrossApplicationAccess.RequestJwtAuthorizationGrantAsync(options, TestContext.Current.CancellationToken);

        var formParams = ParseFormData(capturedBody!);
        Assert.False(formParams.ContainsKey("client_secret"));
        Assert.False(formParams.ContainsKey("scope"));
    }

    [Fact]
    public async Task RequestJwtAuthorizationGrantAsync_ServerError_ThrowsCrossApplicationAccessException()
    {
        _mockHandler.Handler = _ => JsonResponse(HttpStatusCode.BadRequest, new JsonObject
        {
            ["error"] = "invalid_request",
            ["error_description"] = "Missing required parameter: subject_token",
        });

        var options = new RequestJwtAuthGrantOptions
        {
            TokenEndpoint = "https://idp.example.com/token",
            Audience = "https://auth.example.com",
            Resource = "https://resource.example.com",
            IdToken = "test-id-token",
            ClientId = "test-client-id",
            HttpClient = _httpClient,
        };

        var ex = await Assert.ThrowsAsync<CrossApplicationAccessException>(
            () => CrossApplicationAccess.RequestJwtAuthorizationGrantAsync(options, TestContext.Current.CancellationToken));
        Assert.Equal("invalid_request", ex.ErrorCode);
        Assert.Equal("Missing required parameter: subject_token", ex.ErrorDescription);
        Assert.Contains("400", ex.Message);
    }

    [Fact]
    public async Task RequestJwtAuthorizationGrantAsync_WrongIssuedTokenType_ThrowsException()
    {
        _mockHandler.Handler = _ => JsonResponse(HttpStatusCode.OK, new JsonObject
        {
            ["access_token"] = "test-jag",
            ["issued_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
            ["token_type"] = "N_A",
        });

        var options = new RequestJwtAuthGrantOptions
        {
            TokenEndpoint = "https://idp.example.com/token",
            Audience = "https://auth.example.com",
            Resource = "https://resource.example.com",
            IdToken = "test-id-token",
            ClientId = "test-client-id",
            HttpClient = _httpClient,
        };

        var ex = await Assert.ThrowsAsync<CrossApplicationAccessException>(
            () => CrossApplicationAccess.RequestJwtAuthorizationGrantAsync(options, TestContext.Current.CancellationToken));
        Assert.Contains("issued_token_type", ex.Message);
        Assert.Contains(CrossApplicationAccess.TokenTypeIdJag, ex.Message);
    }

    [Fact]
    public async Task RequestJwtAuthorizationGrantAsync_WrongTokenType_ThrowsException()
    {
        _mockHandler.Handler = _ => JsonResponse(HttpStatusCode.OK, new JsonObject
        {
            ["access_token"] = "test-jag",
            ["issued_token_type"] = CrossApplicationAccess.TokenTypeIdJag,
            ["token_type"] = "Bearer",
        });

        var options = new RequestJwtAuthGrantOptions
        {
            TokenEndpoint = "https://idp.example.com/token",
            Audience = "https://auth.example.com",
            Resource = "https://resource.example.com",
            IdToken = "test-id-token",
            ClientId = "test-client-id",
            HttpClient = _httpClient,
        };

        var ex = await Assert.ThrowsAsync<CrossApplicationAccessException>(
            () => CrossApplicationAccess.RequestJwtAuthorizationGrantAsync(options, TestContext.Current.CancellationToken));
        Assert.Contains("token_type", ex.Message);
        Assert.Contains("N_A", ex.Message);
    }

    [Fact]
    public async Task RequestJwtAuthorizationGrantAsync_TokenTypeNa_CaseInsensitive()
    {
        _mockHandler.Handler = _ => JsonResponse(HttpStatusCode.OK, new JsonObject
        {
            ["access_token"] = "test-jag",
            ["issued_token_type"] = CrossApplicationAccess.TokenTypeIdJag,
            ["token_type"] = "n_a",
        });

        var options = new RequestJwtAuthGrantOptions
        {
            TokenEndpoint = "https://idp.example.com/token",
            Audience = "https://auth.example.com",
            Resource = "https://resource.example.com",
            IdToken = "test-id-token",
            ClientId = "test-client-id",
            HttpClient = _httpClient,
        };

        var jag = await CrossApplicationAccess.RequestJwtAuthorizationGrantAsync(options, TestContext.Current.CancellationToken);
        Assert.Equal("test-jag", jag);
    }

    [Fact]
    public async Task RequestJwtAuthorizationGrantAsync_MissingAccessToken_ThrowsException()
    {
        _mockHandler.Handler = _ => JsonResponse(HttpStatusCode.OK, new JsonObject
        {
            ["issued_token_type"] = CrossApplicationAccess.TokenTypeIdJag,
            ["token_type"] = "N_A",
        });

        var options = new RequestJwtAuthGrantOptions
        {
            TokenEndpoint = "https://idp.example.com/token",
            Audience = "https://auth.example.com",
            Resource = "https://resource.example.com",
            IdToken = "test-id-token",
            ClientId = "test-client-id",
            HttpClient = _httpClient,
        };

        var ex = await Assert.ThrowsAsync<CrossApplicationAccessException>(
            () => CrossApplicationAccess.RequestJwtAuthorizationGrantAsync(options, TestContext.Current.CancellationToken));
        Assert.Contains("access_token", ex.Message);
    }

    [Fact]
    public async Task RequestJwtAuthorizationGrantAsync_NullOptions_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => CrossApplicationAccess.RequestJwtAuthorizationGrantAsync(null!, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("", "https://a.com", "https://r.com", "token", "client")]
    [InlineData("https://t.com", "", "https://r.com", "token", "client")]
    [InlineData("https://t.com", "https://a.com", "", "token", "client")]
    [InlineData("https://t.com", "https://a.com", "https://r.com", "", "client")]
    [InlineData("https://t.com", "https://a.com", "https://r.com", "token", "")]
    public async Task RequestJwtAuthorizationGrantAsync_MissingRequiredField_ThrowsArgumentException(
        string tokenEndpoint, string audience, string resource, string idToken, string clientId)
    {
        var options = new RequestJwtAuthGrantOptions
        {
            TokenEndpoint = tokenEndpoint,
            Audience = audience,
            Resource = resource,
            IdToken = idToken,
            ClientId = clientId,
            HttpClient = _httpClient,
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => CrossApplicationAccess.RequestJwtAuthorizationGrantAsync(options, TestContext.Current.CancellationToken));
    }

    #endregion

    #region ExchangeJwtBearerGrantAsync Tests

    [Fact]
    public async Task ExchangeJwtBearerGrantAsync_SuccessfulExchange_ReturnsTokenContainer()
    {
        _mockHandler.Handler = _ => JsonResponse(HttpStatusCode.OK, new JsonObject
        {
            ["access_token"] = "mcp-access-token",
            ["token_type"] = "Bearer",
            ["expires_in"] = 3600,
            ["refresh_token"] = "mcp-refresh-token",
            ["scope"] = "mcp:read mcp:write",
        });

        var options = new ExchangeJwtBearerGrantOptions
        {
            TokenEndpoint = "https://auth.mcp-server.example.com/token",
            Assertion = "test-jag-assertion",
            ClientId = "mcp-client-id",
            HttpClient = _httpClient,
        };

        var tokens = await CrossApplicationAccess.ExchangeJwtBearerGrantAsync(options, TestContext.Current.CancellationToken);

        Assert.Equal("mcp-access-token", tokens.AccessToken);
        Assert.Equal("Bearer", tokens.TokenType);
        Assert.Equal(3600, tokens.ExpiresIn);
        Assert.Equal("mcp-refresh-token", tokens.RefreshToken);
        Assert.Equal("mcp:read mcp:write", tokens.Scope);
    }

    [Fact]
    public async Task ExchangeJwtBearerGrantAsync_SendsCorrectFormData()
    {
        string? capturedBody = null;
        _mockHandler.AsyncHandler = async request =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.OK, new JsonObject
            {
                ["access_token"] = "token",
                ["token_type"] = "Bearer",
            });
        };

        var options = new ExchangeJwtBearerGrantOptions
        {
            TokenEndpoint = "https://auth.example.com/token",
            Assertion = "my-jag-assertion",
            ClientId = "my-client-id",
            ClientSecret = "my-client-secret",
            Scope = "read write",
            HttpClient = _httpClient,
        };

        await CrossApplicationAccess.ExchangeJwtBearerGrantAsync(options, TestContext.Current.CancellationToken);

        Assert.NotNull(capturedBody);
        var formParams = ParseFormData(capturedBody!);
        Assert.Equal(CrossApplicationAccess.GrantTypeJwtBearer, formParams["grant_type"]);
        Assert.Equal("my-jag-assertion", formParams["assertion"]);
        Assert.Equal("my-client-id", formParams["client_id"]);
        Assert.Equal("my-client-secret", formParams["client_secret"]);
        Assert.Equal("read write", formParams["scope"]);
    }

    [Fact]
    public async Task ExchangeJwtBearerGrantAsync_ServerError_ThrowsCrossApplicationAccessException()
    {
        _mockHandler.Handler = _ => JsonResponse(HttpStatusCode.Unauthorized, new JsonObject
        {
            ["error"] = "invalid_grant",
            ["error_description"] = "The JAG assertion is expired",
        });

        var options = new ExchangeJwtBearerGrantOptions
        {
            TokenEndpoint = "https://auth.example.com/token",
            Assertion = "expired-jag",
            ClientId = "client-id",
            HttpClient = _httpClient,
        };

        var ex = await Assert.ThrowsAsync<CrossApplicationAccessException>(
            () => CrossApplicationAccess.ExchangeJwtBearerGrantAsync(options, TestContext.Current.CancellationToken));
        Assert.Equal("invalid_grant", ex.ErrorCode);
        Assert.Equal("The JAG assertion is expired", ex.ErrorDescription);
    }

    [Fact]
    public async Task ExchangeJwtBearerGrantAsync_NonBearerTokenType_ThrowsException()
    {
        _mockHandler.Handler = _ => JsonResponse(HttpStatusCode.OK, new JsonObject
        {
            ["access_token"] = "token",
            ["token_type"] = "mac",
        });

        var options = new ExchangeJwtBearerGrantOptions
        {
            TokenEndpoint = "https://auth.example.com/token",
            Assertion = "test-jag",
            ClientId = "client-id",
            HttpClient = _httpClient,
        };

        var ex = await Assert.ThrowsAsync<CrossApplicationAccessException>(
            () => CrossApplicationAccess.ExchangeJwtBearerGrantAsync(options, TestContext.Current.CancellationToken));
        Assert.Contains("token_type", ex.Message);
        Assert.Contains("bearer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExchangeJwtBearerGrantAsync_BearerCaseInsensitive()
    {
        _mockHandler.Handler = _ => JsonResponse(HttpStatusCode.OK, new JsonObject
        {
            ["access_token"] = "token",
            ["token_type"] = "BEARER",
        });

        var options = new ExchangeJwtBearerGrantOptions
        {
            TokenEndpoint = "https://auth.example.com/token",
            Assertion = "test-jag",
            ClientId = "client-id",
            HttpClient = _httpClient,
        };

        var tokens = await CrossApplicationAccess.ExchangeJwtBearerGrantAsync(options, TestContext.Current.CancellationToken);
        Assert.Equal("token", tokens.AccessToken);
    }

    [Fact]
    public async Task ExchangeJwtBearerGrantAsync_MissingAccessToken_ThrowsException()
    {
        _mockHandler.Handler = _ => JsonResponse(HttpStatusCode.OK, new JsonObject
        {
            ["token_type"] = "Bearer",
        });

        var options = new ExchangeJwtBearerGrantOptions
        {
            TokenEndpoint = "https://auth.example.com/token",
            Assertion = "test-jag",
            ClientId = "client-id",
            HttpClient = _httpClient,
        };

        var ex = await Assert.ThrowsAsync<CrossApplicationAccessException>(
            () => CrossApplicationAccess.ExchangeJwtBearerGrantAsync(options, TestContext.Current.CancellationToken));
        Assert.Contains("access_token", ex.Message);
    }

    [Fact]
    public async Task ExchangeJwtBearerGrantAsync_NullOptions_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => CrossApplicationAccess.ExchangeJwtBearerGrantAsync(null!, TestContext.Current.CancellationToken));
    }

    #endregion

    #region DiscoverAndRequestJwtAuthorizationGrantAsync Tests

    [Fact]
    public async Task DiscoverAndRequestJwtAuthorizationGrantAsync_WithIdpUrl_DiscoversAndExchanges()
    {
        var expectedJag = "discovered-jag-token";
        var requestCount = 0;
        _mockHandler.Handler = request =>
        {
            requestCount++;
            var url = request.RequestUri!.ToString();

            if (url.Contains(".well-known/openid-configuration"))
            {
                return JsonResponse(HttpStatusCode.OK, new JsonObject
                {
                    ["issuer"] = "https://idp.example.com",
                    ["authorization_endpoint"] = "https://idp.example.com/authorize",
                    ["token_endpoint"] = "https://idp.example.com/oauth2/token",
                });
            }

            if (url.Contains("/oauth2/token"))
            {
                return JsonResponse(HttpStatusCode.OK, new JsonObject
                {
                    ["access_token"] = expectedJag,
                    ["issued_token_type"] = CrossApplicationAccess.TokenTypeIdJag,
                    ["token_type"] = "N_A",
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var options = new DiscoverAndRequestJwtAuthGrantOptions
        {
            IdpUrl = "https://idp.example.com",
            Audience = "https://auth.mcp-server.example.com",
            Resource = "https://mcp-server.example.com",
            IdToken = "test-id-token",
            ClientId = "test-client-id",
            HttpClient = _httpClient,
        };

        var jag = await CrossApplicationAccess.DiscoverAndRequestJwtAuthorizationGrantAsync(options, TestContext.Current.CancellationToken);

        Assert.Equal(expectedJag, jag);
        Assert.True(requestCount >= 2, "Should make at least 2 requests (discovery + exchange)");
    }

    [Fact]
    public async Task DiscoverAndRequestJwtAuthorizationGrantAsync_WithDirectTokenEndpoint_SkipsDiscovery()
    {
        _mockHandler.Handler = request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains(".well-known"))
            {
                throw new InvalidOperationException("Should not attempt discovery when IdpTokenEndpoint is provided");
            }

            return JsonResponse(HttpStatusCode.OK, new JsonObject
            {
                ["access_token"] = "direct-jag",
                ["issued_token_type"] = CrossApplicationAccess.TokenTypeIdJag,
                ["token_type"] = "N_A",
            });
        };

        var options = new DiscoverAndRequestJwtAuthGrantOptions
        {
            IdpTokenEndpoint = "https://idp.example.com/oauth2/token",
            Audience = "https://auth.example.com",
            Resource = "https://resource.example.com",
            IdToken = "test-id-token",
            ClientId = "test-client-id",
            HttpClient = _httpClient,
        };

        var jag = await CrossApplicationAccess.DiscoverAndRequestJwtAuthorizationGrantAsync(options, TestContext.Current.CancellationToken);

        Assert.Equal("direct-jag", jag);
    }

    [Fact]
    public async Task DiscoverAndRequestJwtAuthorizationGrantAsync_DiscoveryFails_ThrowsException()
    {
        _mockHandler.Handler = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var options = new DiscoverAndRequestJwtAuthGrantOptions
        {
            IdpUrl = "https://idp.example.com",
            Audience = "https://auth.example.com",
            Resource = "https://resource.example.com",
            IdToken = "test-id-token",
            ClientId = "test-client-id",
            HttpClient = _httpClient,
        };

        await Assert.ThrowsAsync<CrossApplicationAccessException>(
            () => CrossApplicationAccess.DiscoverAndRequestJwtAuthorizationGrantAsync(options, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DiscoverAndRequestJwtAuthorizationGrantAsync_NoIdpUrlOrTokenEndpoint_ThrowsException()
    {
        var options = new DiscoverAndRequestJwtAuthGrantOptions
        {
            Audience = "https://auth.example.com",
            Resource = "https://resource.example.com",
            IdToken = "test-id-token",
            ClientId = "test-client-id",
            HttpClient = _httpClient,
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => CrossApplicationAccess.DiscoverAndRequestJwtAuthorizationGrantAsync(options, TestContext.Current.CancellationToken));
    }

    #endregion

    #region CrossApplicationAccessProvider Tests

    [Fact]
    public async Task CrossApplicationAccessProvider_FullFlow_ReturnsAccessToken()
    {
        _mockHandler.Handler = request =>
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains(".well-known/openid-configuration"))
            {
                return JsonResponse(HttpStatusCode.OK, new JsonObject
                {
                    ["issuer"] = "https://auth.mcp-server.example.com",
                    ["authorization_endpoint"] = "https://auth.mcp-server.example.com/authorize",
                    ["token_endpoint"] = "https://auth.mcp-server.example.com/token",
                });
            }

            if (url.Contains("idp.example.com/token"))
            {
                return JsonResponse(HttpStatusCode.OK, new JsonObject
                {
                    ["access_token"] = "mock-jag-assertion",
                    ["issued_token_type"] = CrossApplicationAccess.TokenTypeIdJag,
                    ["token_type"] = "N_A",
                });
            }

            if (url.Contains("auth.mcp-server.example.com/token"))
            {
                return JsonResponse(HttpStatusCode.OK, new JsonObject
                {
                    ["access_token"] = "final-access-token",
                    ["token_type"] = "Bearer",
                    ["expires_in"] = 3600,
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var provider = new CrossApplicationAccessProvider(
            new CrossApplicationAccessProviderOptions
            {
                ClientId = "mcp-client-id",
                IdpTokenEndpoint = "https://idp.example.com/token",
                IdpClientId = "idp-client-id",
                IdTokenCallback = (context, ct) =>
                {
                    Assert.Equal(new Uri("https://mcp-server.example.com"), context.ResourceUrl);
                    Assert.Equal(new Uri("https://auth.mcp-server.example.com"), context.AuthorizationServerUrl);
                    return Task.FromResult("mock-id-token");
                },
            },
            _httpClient);

        var tokens = await provider.GetAccessTokenAsync(
            resourceUrl: new Uri("https://mcp-server.example.com"),
            authorizationServerUrl: new Uri("https://auth.mcp-server.example.com"),
            TestContext.Current.CancellationToken);

        Assert.Equal("final-access-token", tokens.AccessToken);
        Assert.Equal("Bearer", tokens.TokenType);
        Assert.Equal(3600, tokens.ExpiresIn);
    }

    [Fact]
    public async Task CrossApplicationAccessProvider_CachesTokens()
    {
        var mcpTokenCallCount = 0;
        _mockHandler.Handler = request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains(".well-known"))
            {
                return JsonResponse(HttpStatusCode.OK, new JsonObject
                {
                    ["authorization_endpoint"] = "https://auth.example.com/authorize",
                    ["token_endpoint"] = "https://auth.example.com/token",
                });
            }

            if (url.Contains("idp.example.com"))
            {
                return JsonResponse(HttpStatusCode.OK, new JsonObject
                {
                    ["access_token"] = "mock-jag",
                    ["issued_token_type"] = CrossApplicationAccess.TokenTypeIdJag,
                    ["token_type"] = "N_A",
                });
            }

            mcpTokenCallCount++;
            return JsonResponse(HttpStatusCode.OK, new JsonObject
            {
                ["access_token"] = "cached-token",
                ["token_type"] = "Bearer",
                ["expires_in"] = 3600,
            });
        };

        var idTokenCallCount = 0;
        var provider = new CrossApplicationAccessProvider(
            new CrossApplicationAccessProviderOptions
            {
                ClientId = "client-id",
                IdpTokenEndpoint = "https://idp.example.com/token",
                IdpClientId = "idp-client-id",
                IdTokenCallback = (_, _) =>
                {
                    idTokenCallCount++;
                    return Task.FromResult("mock-id-token");
                },
            },
            _httpClient);

        var ct = TestContext.Current.CancellationToken;

        var firstTokens = await provider.GetAccessTokenAsync(
            new Uri("https://resource.example.com"),
            new Uri("https://auth.example.com"), ct);

        var secondTokens = await provider.GetAccessTokenAsync(
            new Uri("https://resource.example.com"),
            new Uri("https://auth.example.com"), ct);

        Assert.Same(firstTokens, secondTokens);
        Assert.Equal(1, idTokenCallCount);
        Assert.Equal(1, mcpTokenCallCount);
    }

    [Fact]
    public async Task CrossApplicationAccessProvider_InvalidateCache_ForcesRefresh()
    {
        var idTokenCallCount = 0;
        _mockHandler.Handler = request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains(".well-known"))
            {
                return JsonResponse(HttpStatusCode.OK, new JsonObject
                {
                    ["authorization_endpoint"] = "https://auth.example.com/authorize",
                    ["token_endpoint"] = "https://auth.example.com/token",
                });
            }

            if (url.Contains("idp.example.com"))
            {
                return JsonResponse(HttpStatusCode.OK, new JsonObject
                {
                    ["access_token"] = "mock-jag",
                    ["issued_token_type"] = CrossApplicationAccess.TokenTypeIdJag,
                    ["token_type"] = "N_A",
                });
            }

            return JsonResponse(HttpStatusCode.OK, new JsonObject
            {
                ["access_token"] = $"token-{idTokenCallCount}",
                ["token_type"] = "Bearer",
                ["expires_in"] = 3600,
            });
        };

        var provider = new CrossApplicationAccessProvider(
            new CrossApplicationAccessProviderOptions
            {
                ClientId = "client-id",
                IdpTokenEndpoint = "https://idp.example.com/token",
                IdpClientId = "idp-client-id",
                IdTokenCallback = (_, _) =>
                {
                    idTokenCallCount++;
                    return Task.FromResult("mock-id-token");
                },
            },
            _httpClient);

        var ct = TestContext.Current.CancellationToken;

        await provider.GetAccessTokenAsync(
            new Uri("https://resource.example.com"),
            new Uri("https://auth.example.com"), ct);

        provider.InvalidateCache();

        await provider.GetAccessTokenAsync(
            new Uri("https://resource.example.com"),
            new Uri("https://auth.example.com"), ct);

        Assert.Equal(2, idTokenCallCount);
    }

    [Fact]
    public async Task CrossApplicationAccessProvider_IdTokenCallbackReturnsEmpty_ThrowsException()
    {
        _mockHandler.Handler = request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains(".well-known"))
            {
                return JsonResponse(HttpStatusCode.OK, new JsonObject
                {
                    ["authorization_endpoint"] = "https://auth.example.com/authorize",
                    ["token_endpoint"] = "https://auth.example.com/token",
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var provider = new CrossApplicationAccessProvider(
            new CrossApplicationAccessProviderOptions
            {
                ClientId = "client-id",
                IdpTokenEndpoint = "https://idp.example.com/token",
                IdpClientId = "idp-client-id",
                IdTokenCallback = (_, _) => Task.FromResult(string.Empty),
            },
            _httpClient);

        await Assert.ThrowsAsync<CrossApplicationAccessException>(
            () => provider.GetAccessTokenAsync(
                new Uri("https://resource.example.com"),
                new Uri("https://auth.example.com"),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CrossApplicationAccessProvider_NullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new CrossApplicationAccessProvider(null!));
    }

    [Fact]
    public void CrossApplicationAccessProvider_MissingClientId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new CrossApplicationAccessProvider(
            new CrossApplicationAccessProviderOptions
            {
                ClientId = "",
                IdpTokenEndpoint = "https://idp.example.com/token",
                IdpClientId = "idp-client-id",
                IdTokenCallback = (_, _) => Task.FromResult("test"),
            }));
    }

    [Fact]
    public void CrossApplicationAccessProvider_MissingIdTokenCallback_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new CrossApplicationAccessProvider(
            new CrossApplicationAccessProviderOptions
            {
                ClientId = "client-id",
                IdpTokenEndpoint = "https://idp.example.com/token",
                IdpClientId = "idp-client-id",
                IdTokenCallback = null!,
            }));
    }

    [Fact]
    public void CrossApplicationAccessProvider_MissingIdpConfig_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new CrossApplicationAccessProvider(
            new CrossApplicationAccessProviderOptions
            {
                ClientId = "client-id",
                IdpClientId = "idp-client-id",
                // Neither IdpUrl nor IdpTokenEndpoint provided
                IdTokenCallback = (_, _) => Task.FromResult("test"),
            }));
    }

    #endregion

    #region CrossApplicationAccessException Tests

    [Fact]
    public void CrossApplicationAccessException_WithErrorCodeAndDescription_FormatsMessage()
    {
        var ex = new CrossApplicationAccessException("Base message", "invalid_grant", "Token expired");

        Assert.Contains("Base message", ex.Message);
        Assert.Contains("invalid_grant", ex.Message);
        Assert.Contains("Token expired", ex.Message);
        Assert.Equal("invalid_grant", ex.ErrorCode);
        Assert.Equal("Token expired", ex.ErrorDescription);
    }

    [Fact]
    public void CrossApplicationAccessException_WithErrorUri_StoresIt()
    {
        var ex = new CrossApplicationAccessException("msg", "error", "desc", "https://docs.example.com/error");

        Assert.Equal("https://docs.example.com/error", ex.ErrorUri);
    }

    [Fact]
    public void CrossApplicationAccessException_WithoutErrorDetails_PlainMessage()
    {
        var ex = new CrossApplicationAccessException("Simple error");

        Assert.Equal("Simple error", ex.Message);
        Assert.Null(ex.ErrorCode);
        Assert.Null(ex.ErrorDescription);
        Assert.Null(ex.ErrorUri);
    }

    #endregion

    #region Constants Tests

    [Fact]
    public void Constants_AreCorrectValues()
    {
        Assert.Equal("urn:ietf:params:oauth:grant-type:token-exchange", CrossApplicationAccess.GrantTypeTokenExchange);
        Assert.Equal("urn:ietf:params:oauth:grant-type:jwt-bearer", CrossApplicationAccess.GrantTypeJwtBearer);
        Assert.Equal("urn:ietf:params:oauth:token-type:id_token", CrossApplicationAccess.TokenTypeIdToken);
        Assert.Equal("urn:ietf:params:oauth:token-type:saml2", CrossApplicationAccess.TokenTypeSaml2);
        Assert.Equal("urn:ietf:params:oauth:token-type:id-jag", CrossApplicationAccess.TokenTypeIdJag);
        Assert.Equal("N_A", CrossApplicationAccess.TokenTypeNotApplicable);
    }

    #endregion

    #region Helpers

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, JsonObject payload)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(payload.ToJsonString(), System.Text.Encoding.UTF8, "application/json")
        };
    }

    private static Dictionary<string, string> ParseFormData(string formData)
    {
        var result = new Dictionary<string, string>();
        foreach (var pair in formData.Split('&'))
        {
            var idx = pair.IndexOf('=');
            if (idx >= 0)
            {
                var key = pair.Substring(0, idx);
                var value = pair.Substring(idx + 1);
                result[Uri.UnescapeDataString(key.Replace('+', ' '))] = Uri.UnescapeDataString(value.Replace('+', ' '));
            }
        }
        return result;
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? Handler { get; set; }
        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? AsyncHandler { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (AsyncHandler is not null)
            {
                return await AsyncHandler(request);
            }

            if (Handler is not null)
            {
                return Handler(request);
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("No mock response configured")
            };
        }
    }

    #endregion
}
