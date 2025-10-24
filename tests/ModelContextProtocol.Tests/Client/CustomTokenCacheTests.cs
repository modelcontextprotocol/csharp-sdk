using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Authentication;
using System.Text.Json;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json.Nodes;
using System.Linq.Expressions;

namespace ModelContextProtocol.Tests.Client;

public class CustomTokenCacheTests
{
    [Fact]
    public async Task GetTokenAsync_CachedAccessTokenIsUsedForOutgoingRequests()
    {
        // Arrange
        var cachedAccessToken = $"my_access_token_{Guid.NewGuid()}";

        var tokenCacheMock = new Mock<ITokenCache>();
        MockCachedAccessToken(tokenCacheMock, cachedAccessToken);

        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        MockInitializeResponse(httpMessageHandlerMock);

        var httpClientTransport = new HttpClientTransport(
            transportOptions: NewHttpClientTransportOptions(tokenCacheMock.Object),
            httpClient: new HttpClient(httpMessageHandlerMock.Object));

        var connectedTransport = await httpClientTransport.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var initializeRequest = new JsonRpcRequest { Method = RequestMethods.Initialize, Id = new RequestId(1) };
        await connectedTransport.SendMessageAsync(initializeRequest, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        httpMessageHandlerMock
            .Protected()
            .Verify("SendAsync", Times.AtLeastOnce(), ItExpr.Is<HttpRequestMessage>(req =>
                   req.RequestUri == new Uri("http://localhost:1337/")
                && req.Headers.Authorization != null
                && req.Headers.Authorization.Scheme == "Bearer"
                && req.Headers.Authorization.Parameter == cachedAccessToken
            ), ItExpr.IsAny<CancellationToken>());

        httpMessageHandlerMock
            .Protected()
            .Verify("SendAsync", Times.Never(), ItExpr.Is<HttpRequestMessage>(req =>
                   req.RequestUri == new Uri("http://localhost:1337/")
                && (req.Headers.Authorization == null || req.Headers.Authorization.Parameter != cachedAccessToken)
            ), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task StoreTokenAsync_NewlyAcquiredAccessTokenIsCached()
    {
        // Arrange
        var tokenCacheMock = new Mock<ITokenCache>();
        MockNoAccessTokenUntilStored(tokenCacheMock);

        var newAccessToken = $"new_access_token_{Guid.NewGuid()}";

        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        MockUnauthorizedResponse(httpMessageHandlerMock);
        MockProtectedResourceMetadataResponse(httpMessageHandlerMock);
        MockAuthorizationServerMetadataResponse(httpMessageHandlerMock);
        MockAccessTokenResponse(httpMessageHandlerMock, newAccessToken);
        MockInitializeResponse(httpMessageHandlerMock);

        var httpClientTransport = new HttpClientTransport(
            transportOptions: NewHttpClientTransportOptions(tokenCacheMock.Object),
            httpClient: new HttpClient(httpMessageHandlerMock.Object));

        var connectedTransport = await httpClientTransport.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var initializeRequest = new JsonRpcRequest { Method = RequestMethods.Initialize, Id = new RequestId(1) };
        await connectedTransport.SendMessageAsync(initializeRequest, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        tokenCacheMock
            .Verify(tc => tc.StoreTokenAsync(
                It.Is<TokenContainerCacheable>(token => token.AccessToken == newAccessToken),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    static HttpClientTransportOptions NewHttpClientTransportOptions(ITokenCache? tokenCache = null) => new()
    {
        Name = "MCP Server",
        Endpoint = new Uri("http://localhost:1337/"),
        TransportMode = HttpTransportMode.StreamableHttp,
        OAuth = new()
        {
            ClientId = "mcp_inspector",
            RedirectUri = new Uri("http://localhost:6274/oauth/callback"),
            Scopes = ["openid", "profile", "offline_access"],
            AuthorizationRedirectDelegate = (authorizationUrl, redirectUri, cancellationToken) => Task.FromResult<string?>($"auth_code_{Guid.NewGuid()}"),
            TokenCache = tokenCache,
        },
    };

    static void MockCachedAccessToken(Mock<ITokenCache> tokenCache, string cachedAccessToken)
    {
        tokenCache
            .Setup(tc => tc.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenContainerCacheable
            {
                AccessToken = cachedAccessToken,
                ObtainedAt = DateTimeOffset.UtcNow,
                ExpiresIn = (int)TimeSpan.FromHours(1).TotalSeconds,
            });
    }

    static void MockNoAccessTokenUntilStored(Mock<ITokenCache> tokenCache)
    {
        tokenCache
            .Setup(tc => tc.StoreTokenAsync(It.IsAny<TokenContainerCacheable>(), It.IsAny<CancellationToken>()))
            .Callback<TokenContainerCacheable, CancellationToken>((token, ct) =>
            {
                // Simulate that the token is now cached
                MockCachedAccessToken(tokenCache, token.AccessToken);
            })
            .Returns(default(ValueTask));
    }

    static void MockUnauthorizedResponse(Mock<HttpMessageHandler> httpMessageHandler)
    {
        MockHttpResponse(httpMessageHandler,
            request: req => req.RequestUri == new Uri("http://localhost:1337/") 
                && req.Method == HttpMethod.Post
                && (req.Headers.Authorization == null || string.IsNullOrWhiteSpace(req.Headers.Authorization.Parameter)),
            response: new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Headers = {
                    { "WWW-Authenticate", "Bearer realm=\"Bearer\", resource_metadata=\"http://localhost:1337/.well-known/oauth-protected-resource\"" }
                },
            });
    }

    static void MockProtectedResourceMetadataResponse(Mock<HttpMessageHandler> httpMessageHandler)
    {
        MockHttpResponse(httpMessageHandler,
            request: req => req.RequestUri == new Uri("http://localhost:1337/.well-known/oauth-protected-resource"),
            response: new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = ToJsonContent(new
                {
                    resource = "http://localhost:1337/",
                    authorization_servers = new[] { "http://localhost:1336/" },
                })
            });
    }

    static void MockAuthorizationServerMetadataResponse(Mock<HttpMessageHandler> httpMessageHandler)
    {
        MockHttpResponse(httpMessageHandler,
            request: req => req.RequestUri == new Uri("http://localhost:1336/.well-known/openid-configuration"),
            response: new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = ToJsonContent(new
                {
                    authorization_endpoint = "http://localhost:1336/connect/authorize",
                    token_endpoint = "http://localhost:1336/connect/token",
                })
            });
    }

    static void MockAccessTokenResponse(Mock<HttpMessageHandler> httpMessageHandler, string accessToken)
    {
        MockHttpResponse(httpMessageHandler,
            request: req => req.RequestUri == new Uri("http://localhost:1336/connect/token"),
            response: new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = ToJsonContent(new
                {
                    access_token = accessToken,
                })
            });
    }

    static void MockInitializeResponse(Mock<HttpMessageHandler> httpMessageHandler)
    {
        MockHttpResponse(httpMessageHandler,
            request: req => req.RequestUri == new Uri("http://localhost:1337/")
                && req.Method == HttpMethod.Post
                && req.Headers.Authorization != null
                && req.Headers.Authorization.Scheme == "Bearer"
                && !string.IsNullOrWhiteSpace(req.Headers.Authorization.Parameter),
            response: new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = ToJsonContent(new JsonRpcResponse
                {
                    Id = new RequestId(1),
                    Result = ToJson(new InitializeResult
                    {
                        ProtocolVersion = "2024-11-05",
                        Capabilities = new ServerCapabilities
                        {
                            Prompts = new PromptsCapability { ListChanged = true },
                            Resources = new ResourcesCapability { Subscribe = true, ListChanged = true },
                            Tools = new ToolsCapability { ListChanged = true },
                            Logging = new LoggingCapability(),
                            Completions = new CompletionsCapability(),
                        },
                        ServerInfo = new Implementation
                        {
                            Name = "mcp-test-server",
                            Version = "1.0.0"
                        },
                        Instructions = "This server provides weather information and file system access."
                    })
                }),
            });
    }

    static void MockHttpResponse(Mock<HttpMessageHandler> httpMessageHandler, Expression<Func<HttpRequestMessage, bool>>? request = null, HttpResponseMessage? response = null)
    {
        httpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", request != null ? ItExpr.Is(request) : ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response ?? new HttpResponseMessage());
    }

    static StringContent ToJsonContent<T>(T content) => new(
        content: JsonSerializer.Serialize(content, McpJsonUtilities.DefaultOptions),
        encoding: System.Text.Encoding.UTF8,
        mediaType: "application/json");

    static JsonNode? ToJson<T>(T content) => JsonSerializer.SerializeToNode(
        value: content,
        options: McpJsonUtilities.DefaultOptions);
}
