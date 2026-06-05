using System.Net;
using System.Text.Json.Nodes;
using ModelContextProtocol.Authentication;

namespace ModelContextProtocol.Tests;

public sealed class IdentityAssertionGrantTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HttpClient _httpClient;

    public IdentityAssertionGrantTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _mockHandler.Dispose();
    }

    #region IdentityAssertionGrantProvider Tests

    [Fact]
    public async Task IdentityAssertionGrantProvider_FullFlow_ReturnsAccessToken()
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
                    ["issued_token_type"] = "urn:ietf:params:oauth:token-type:id-jag",
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

        var provider = new IdentityAssertionGrantProvider(
            new IdentityAssertionGrantProviderOptions
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
    public async Task IdentityAssertionGrantProvider_CachesTokens()
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
                    ["issued_token_type"] = "urn:ietf:params:oauth:token-type:id-jag",
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
        var provider = new IdentityAssertionGrantProvider(
            new IdentityAssertionGrantProviderOptions
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
    public async Task IdentityAssertionGrantProvider_InvalidateCache_ForcesRefresh()
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
                    ["issued_token_type"] = "urn:ietf:params:oauth:token-type:id-jag",
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

        var provider = new IdentityAssertionGrantProvider(
            new IdentityAssertionGrantProviderOptions
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
    public async Task IdentityAssertionGrantProvider_IdTokenCallbackReturnsEmpty_ThrowsException()
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

        var provider = new IdentityAssertionGrantProvider(
            new IdentityAssertionGrantProviderOptions
            {
                ClientId = "client-id",
                IdpTokenEndpoint = "https://idp.example.com/token",
                IdpClientId = "idp-client-id",
                IdTokenCallback = (_, _) => Task.FromResult(string.Empty),
            },
            _httpClient);

        await Assert.ThrowsAsync<IdentityAssertionGrantException>(
            () => provider.GetAccessTokenAsync(
                new Uri("https://resource.example.com"),
                new Uri("https://auth.example.com"),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public void IdentityAssertionGrantProvider_NullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new IdentityAssertionGrantProvider(null!, _httpClient));
    }

    [Fact]
    public void IdentityAssertionGrantProvider_NullHttpClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new IdentityAssertionGrantProvider(
            new IdentityAssertionGrantProviderOptions
            {
                ClientId = "client-id",
                IdpTokenEndpoint = "https://idp.example.com/token",
                IdpClientId = "idp-client-id",
                IdTokenCallback = (_, _) => Task.FromResult("token"),
            },
            null!));
    }

    [Fact]
    public void IdentityAssertionGrantProvider_MissingClientId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new IdentityAssertionGrantProvider(
            new IdentityAssertionGrantProviderOptions
            {
                ClientId = "",
                IdpTokenEndpoint = "https://idp.example.com/token",
                IdpClientId = "idp-client-id",
                IdTokenCallback = (_, _) => Task.FromResult("test"),
            },
            _httpClient));
    }

    [Fact]
    public void IdentityAssertionGrantProvider_MissingIdTokenCallback_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new IdentityAssertionGrantProvider(
            new IdentityAssertionGrantProviderOptions
            {
                ClientId = "client-id",
                IdpTokenEndpoint = "https://idp.example.com/token",
                IdpClientId = "idp-client-id",
                IdTokenCallback = null!,
            },
            _httpClient));
    }

    [Fact]
    public void IdentityAssertionGrantProvider_MissingIdpConfig_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new IdentityAssertionGrantProvider(
            new IdentityAssertionGrantProviderOptions
            {
                ClientId = "client-id",
                IdpClientId = "idp-client-id",
                // Neither IdpUrl nor IdpTokenEndpoint provided
                IdTokenCallback = (_, _) => Task.FromResult("test"),
            },
            _httpClient));
    }

    #endregion

    #region IdentityAssertionGrantException Tests

    [Fact]
    public void IdentityAssertionGrantException_WithErrorCodeAndDescription_FormatsMessage()
    {
        var ex = new IdentityAssertionGrantException("Base message", "invalid_grant", "Token expired");

        Assert.Contains("Base message", ex.Message);
        Assert.Contains("invalid_grant", ex.Message);
        Assert.Contains("Token expired", ex.Message);
        Assert.Equal("invalid_grant", ex.ErrorCode);
        Assert.Equal("Token expired", ex.ErrorDescription);
    }

    [Fact]
    public void IdentityAssertionGrantException_WithErrorUri_StoresIt()
    {
        var ex = new IdentityAssertionGrantException("msg", "error", "desc", "https://docs.example.com/error");

        Assert.Equal("https://docs.example.com/error", ex.ErrorUri);
    }

    [Fact]
    public void IdentityAssertionGrantException_WithoutErrorDetails_PlainMessage()
    {
        var ex = new IdentityAssertionGrantException("Simple error");

        Assert.Equal("Simple error", ex.Message);
        Assert.Null(ex.ErrorCode);
        Assert.Null(ex.ErrorDescription);
        Assert.Null(ex.ErrorUri);
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
