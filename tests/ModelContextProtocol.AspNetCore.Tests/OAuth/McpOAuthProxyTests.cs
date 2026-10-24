using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.AspNetCore.Tests.OAuth;

public sealed class McpOAuthProxyTests : KestrelInMemoryTest
{
    private const string ClientRedirectUri = "http://127.0.0.1:43123/callback";
    private const string Resource = "https://mcp.example.com/server";
    private readonly RecordingStore _store = new();
    private readonly StubHttpClientFactory _upstream = new();
    private bool _allowRegistration = true;
    private bool _returnUpstreamAccessToken;
    private bool _useDiscovery;
    private string[] _lastRegistrationGrantTypes = [];
    private string[] _lastMintedScopes = [];
    private int _tokenNumber;

    public McpOAuthProxyTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        SocketsHttpHandler.AllowAutoRedirect = false;
        Builder.Services.AddMcpOAuthProxy(options =>
        {
            options.Issuer = new Uri("http://localhost:5000/oauth");
            if (_useDiscovery)
            {
                options.UpstreamIssuer = new Uri("http://localhost:5000/upstream");
            }
            else
            {
                options.UpstreamAuthorizationEndpoint = new Uri("http://localhost:5000/upstream/authorize");
                options.UpstreamTokenEndpoint = new Uri("http://localhost:5000/upstream/token");
            }
            options.UpstreamClientId = "upstream-client";
            options.UpstreamClientSecret = "upstream-secret";
            options.UpstreamRedirectUri = new Uri("http://localhost:5000/oauth/callback");
            options.AllowedScopes.Add("mcp:tools");
            options.AllowedScopes.Add("mcp:read");
            options.AllowedResources.Add(Resource);
            options.ForwardResourceIndicator = true;
            options.CookieSecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ClientRegistrationValidator = (context, _) =>
            {
                _lastRegistrationGrantTypes = context.GrantTypes.ToArray();
                return ValueTask.FromResult(_allowRegistration);
            };
            options.AuthorizationValidator = static (_, _) => ValueTask.FromResult(true);
            options.TokenFactory = (context, _) =>
            {
                _lastMintedScopes = context.Scopes.ToArray();
                var tokenNumber = Interlocked.Increment(ref _tokenNumber);
                return ValueTask.FromResult(new McpOAuthProxyTokenResult
                {
                    AccessToken = _returnUpstreamAccessToken ? context.UpstreamAccessToken : $"proxy-access-{tokenNumber}",
                    TokenType = "Bearer",
                    ExpiresIn = TimeSpan.FromMinutes(5),
                    Subject = "user-123",
                    Audience = context.Resource ?? Resource,
                    TokenId = $"jti-{tokenNumber}",
                });
            };
        });
        Builder.Services.AddSingleton<IMcpOAuthProxyStore>(_store);
        Builder.Services.AddSingleton<IHttpClientFactory>(_upstream);
        Builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
    }

    [Fact]
    public async Task MetadataAdvertisesPathAwareProxyEndpoints()
    {
        await using var app = await StartServerAsync();

        using var response = await HttpClient.GetAsync(
            "/.well-known/oauth-authorization-server/oauth",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        var root = document.RootElement;
        Assert.Equal("http://localhost:5000/oauth", root.GetProperty("issuer").GetString());
        Assert.Equal("http://localhost:5000/oauth/authorize", root.GetProperty("authorization_endpoint").GetString());
        Assert.Equal("http://localhost:5000/oauth/token", root.GetProperty("token_endpoint").GetString());
        Assert.Equal("http://localhost:5000/oauth/register", root.GetProperty("registration_endpoint").GetString());
        Assert.Equal("S256", root.GetProperty("code_challenge_methods_supported")[0].GetString());
    }

    [Fact]
    public async Task RegistrationRejectsUnsafeRedirectUri()
    {
        await using var app = await StartServerAsync();

        using var response = await HttpClient.PostAsJsonAsync(
            "/oauth/register",
            new OAuthProxyRegistrationTestRequest
            {
                RedirectUris = ["https://client.example/callback#fragment"],
                TokenEndpointAuthMethod = "client_secret_post",
            },
            OAuthProxyTestJsonContext.Default.OAuthProxyRegistrationTestRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        Assert.Equal("invalid_redirect_uri", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task RegistrationPolicyCanRejectDynamicClient()
    {
        _allowRegistration = false;
        await using var app = await StartServerAsync();

        using var response = await HttpClient.PostAsJsonAsync(
            "/oauth/register",
            new OAuthProxyRegistrationTestRequest
            {
                RedirectUris = [ClientRedirectUri],
                TokenEndpointAuthMethod = "none",
            },
            OAuthProxyTestJsonContext.Default.OAuthProxyRegistrationTestRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RegistrationDefaultsToAuthorizationCodeGrant()
    {
        await using var app = await StartServerAsync();

        using var response = await HttpClient.PostAsJsonAsync(
            "/oauth/register",
            new OAuthProxyRegistrationTestRequest
            {
                RedirectUris = [ClientRedirectUri],
                TokenEndpointAuthMethod = "none",
            },
            OAuthProxyTestJsonContext.Default.OAuthProxyRegistrationTestRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        Assert.Equal(["authorization_code"], _lastRegistrationGrantTypes);
        Assert.Equal("authorization_code", document.RootElement.GetProperty("grant_types")[0].GetString());
        Assert.Equal(1, document.RootElement.GetProperty("grant_types").GetArrayLength());
    }

    [Fact]
    public async Task AuthorizationErrorRedirectIncludesIssuer()
    {
        await using var app = await StartServerAsync();
        var registration = await RegisterClientAsync();
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var url = QueryHelpers.AddQueryString(
            "/oauth/authorize",
            new Dictionary<string, string?>
            {
                ["client_id"] = registration.ClientId,
                ["redirect_uri"] = ClientRedirectUri,
                ["response_type"] = "token",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["state"] = "client-state",
            });

        using var response = await HttpClient.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var query = QueryHelpers.ParseQuery(Assert.IsType<Uri>(response.Headers.Location).Query);
        Assert.Equal("unsupported_response_type", query["error"]);
        Assert.Equal("http://localhost:5000/oauth", query["iss"]);
    }

    [Fact]
    public async Task AuthorizationCodeFlowSeparatesTokensAndRotatesRefreshHandle()
    {
        await using var app = await StartServerAsync();
        var registration = await RegisterClientAsync();

        var authorization = await BeginAuthorizationAsync(registration.ClientId);
        Assert.NotEqual(authorization.DownstreamCodeChallenge, authorization.UpstreamCodeChallenge);
        Assert.Equal(Resource, authorization.UpstreamResource);

        var proxyCode = await CompleteCallbackAsync(authorization.TransactionId);
        var firstToken = await ExchangeCodeAsync(registration.ClientId, proxyCode, authorization.CodeVerifier);

        Assert.Equal("proxy-access-1", firstToken.AccessToken);
        Assert.NotEqual("upstream-access-1", firstToken.AccessToken);
        Assert.False(string.IsNullOrEmpty(firstToken.RefreshToken));
        Assert.Equal("no-store", firstToken.CacheControl);

        using var codeReplayResponse = await PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = proxyCode,
            ["client_id"] = registration.ClientId,
            ["redirect_uri"] = ClientRedirectUri,
            ["code_verifier"] = authorization.CodeVerifier,
        });
        Assert.Equal(HttpStatusCode.BadRequest, codeReplayResponse.StatusCode);

        var secondToken = await RefreshAsync(registration.ClientId, firstToken.RefreshToken!);
        Assert.Equal("proxy-access-2", secondToken.AccessToken);
        Assert.NotEqual(firstToken.RefreshToken, secondToken.RefreshToken);
        Assert.Equal(2, _upstream.TokenRequests.Count);
        Assert.Equal("authorization_code", _upstream.TokenRequests[0]["grant_type"]);
        Assert.Equal(Resource, _upstream.TokenRequests[0]["resource"]);
        Assert.Equal("refresh_token", _upstream.TokenRequests[1]["grant_type"]);
        Assert.Equal(Resource, _upstream.TokenRequests[1]["resource"]);

        using var replayResponse = await PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = firstToken.RefreshToken!,
            ["client_id"] = registration.ClientId,
        });
        Assert.Equal(HttpStatusCode.BadRequest, replayResponse.StatusCode);

        using var revokedDescendantResponse = await PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = secondToken.RefreshToken!,
            ["client_id"] = registration.ClientId,
        });
        Assert.Equal(HttpStatusCode.BadRequest, revokedDescendantResponse.StatusCode);

        Assert.All(_store.Values, value =>
        {
            var serialized = Encoding.UTF8.GetString(value.Span);
            Assert.DoesNotContain("upstream-access", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("upstream-refresh", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(ClientRedirectUri, serialized, StringComparison.Ordinal);
        });
        Assert.All(_store.Keys, key =>
        {
            Assert.DoesNotContain(firstToken.RefreshToken!, key, StringComparison.Ordinal);
            Assert.DoesNotContain(secondToken.RefreshToken!, key, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task TokenFactoryReceivesOnlyScopesGrantedByUpstream()
    {
        await using var app = await StartServerAsync();
        var registration = await RegisterClientAsync("mcp:tools mcp:read");
        var authorization = await BeginAuthorizationAsync(registration.ClientId, "mcp:tools mcp:read");
        var proxyCode = await CompleteCallbackAsync(authorization.TransactionId);

        await ExchangeCodeAsync(registration.ClientId, proxyCode, authorization.CodeVerifier);

        Assert.Equal(["mcp:tools"], _lastMintedScopes);
    }

    [Fact]
    public async Task ClientWithoutRefreshGrantDoesNotReceiveRefreshToken()
    {
        await using var app = await StartServerAsync();
        var registration = await RegisterClientAsync(includeRefreshGrant: false);
        var authorization = await BeginAuthorizationAsync(registration.ClientId);
        var proxyCode = await CompleteCallbackAsync(authorization.TransactionId);

        var token = await ExchangeCodeAsync(registration.ClientId, proxyCode, authorization.CodeVerifier);

        Assert.Null(token.RefreshToken);
    }

    [Fact]
    public async Task AuthorizationUsesValidatedOidcDiscovery()
    {
        _useDiscovery = true;
        await using var app = await StartServerAsync();
        var registration = await RegisterClientAsync();

        var authorization = await BeginAuthorizationAsync(registration.ClientId);

        Assert.False(string.IsNullOrEmpty(authorization.TransactionId));
        Assert.Equal(1, _upstream.DiscoveryRequests);
    }

    [Fact]
    public async Task TokenEndpointRejectsUpstreamTokenPassthrough()
    {
        _returnUpstreamAccessToken = true;
        await using var app = await StartServerAsync();
        var registration = await RegisterClientAsync();
        var authorization = await BeginAuthorizationAsync(registration.ClientId);
        var proxyCode = await CompleteCallbackAsync(authorization.TransactionId);

        using var response = await PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = proxyCode,
            ["client_id"] = registration.ClientId,
            ["redirect_uri"] = ClientRedirectUri,
            ["code_verifier"] = authorization.CodeVerifier,
        });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        Assert.Equal("server_error", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task TransientRefreshFailurePreservesProxyRefreshHandleForRetry()
    {
        await using var app = await StartServerAsync();
        var registration = await RegisterClientAsync();
        var authorization = await BeginAuthorizationAsync(registration.ClientId);
        var proxyCode = await CompleteCallbackAsync(authorization.TransactionId);
        var firstToken = await ExchangeCodeAsync(registration.ClientId, proxyCode, authorization.CodeVerifier);

        _upstream.FailNextRefreshRequest = true;
        using var failedResponse = await PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = firstToken.RefreshToken!,
            ["client_id"] = registration.ClientId,
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failedResponse.StatusCode);
        using var error = JsonDocument.Parse(await failedResponse.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        Assert.Equal("temporarily_unavailable", error.RootElement.GetProperty("error").GetString());

        var retriedToken = await RefreshAsync(registration.ClientId, firstToken.RefreshToken!);
        Assert.Equal("proxy-access-2", retriedToken.AccessToken);
        Assert.NotEqual(firstToken.RefreshToken, retriedToken.RefreshToken);
    }

    [Fact]
    public async Task ConcurrentRefreshReplayRevokesInFlightTokenFamily()
    {
        await using var app = await StartServerAsync();
        var registration = await RegisterClientAsync();
        var authorization = await BeginAuthorizationAsync(registration.ClientId);
        var proxyCode = await CompleteCallbackAsync(authorization.TransactionId);
        var firstToken = await ExchangeCodeAsync(registration.ClientId, proxyCode, authorization.CodeVerifier);

        _store.BlockNextRefreshTake = true;
        var refreshParameters = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = firstToken.RefreshToken!,
            ["client_id"] = registration.ClientId,
        };
        var delayedRefresh = PostTokenAsync(refreshParameters);
        await _store.RefreshTakeStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        using var replayResponse = await PostTokenAsync(refreshParameters);
        Assert.Equal(HttpStatusCode.BadRequest, replayResponse.StatusCode);

        _store.ReleaseRefreshTake.TrySetResult(true);
        using var delayedResponse = await delayedRefresh;
        Assert.Equal(HttpStatusCode.BadRequest, delayedResponse.StatusCode);
    }

    [Fact]
    public async Task RefreshReplayDuringUpstreamExchangePreventsDescendantPublication()
    {
        await using var app = await StartServerAsync();
        var registration = await RegisterClientAsync();
        var authorization = await BeginAuthorizationAsync(registration.ClientId);
        var proxyCode = await CompleteCallbackAsync(authorization.TransactionId);
        var firstToken = await ExchangeCodeAsync(registration.ClientId, proxyCode, authorization.CodeVerifier);

        _upstream.BlockNextRefreshRequest = true;
        var refreshParameters = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = firstToken.RefreshToken!,
            ["client_id"] = registration.ClientId,
        };
        var inFlightRefresh = PostTokenAsync(refreshParameters);
        await _upstream.RefreshRequestStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        using var replayResponse = await PostTokenAsync(refreshParameters);
        Assert.Equal(HttpStatusCode.BadRequest, replayResponse.StatusCode);

        _upstream.ReleaseRefreshRequest.TrySetResult(true);
        using var inFlightResponse = await inFlightRefresh;
        Assert.Equal(HttpStatusCode.BadRequest, inFlightResponse.StatusCode);
    }

    [Fact]
    public async Task UpstreamRefreshTimeoutPreservesProxyRefreshHandleForRetry()
    {
        await using var app = await StartServerAsync();
        var registration = await RegisterClientAsync();
        var authorization = await BeginAuthorizationAsync(registration.ClientId);
        var proxyCode = await CompleteCallbackAsync(authorization.TransactionId);
        var firstToken = await ExchangeCodeAsync(registration.ClientId, proxyCode, authorization.CodeVerifier);

        _upstream.ThrowNextRefreshTimeout = true;
        using var timeoutResponse = await PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = firstToken.RefreshToken!,
            ["client_id"] = registration.ClientId,
        });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, timeoutResponse.StatusCode);

        var retriedToken = await RefreshAsync(registration.ClientId, firstToken.RefreshToken!);
        Assert.Equal("proxy-access-2", retriedToken.AccessToken);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task MalformedTransientResponsePreservesProxyRefreshHandleForRetry(HttpStatusCode statusCode)
    {
        await using var app = await StartServerAsync();
        var registration = await RegisterClientAsync();
        var authorization = await BeginAuthorizationAsync(registration.ClientId);
        var proxyCode = await CompleteCallbackAsync(authorization.TransactionId);
        var firstToken = await ExchangeCodeAsync(registration.ClientId, proxyCode, authorization.CodeVerifier);

        _upstream.MalformedRefreshFailureStatus = statusCode;
        using var throttledResponse = await PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = firstToken.RefreshToken!,
            ["client_id"] = registration.ClientId,
        });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, throttledResponse.StatusCode);

        var retriedToken = await RefreshAsync(registration.ClientId, firstToken.RefreshToken!);
        Assert.Equal("proxy-access-2", retriedToken.AccessToken);
    }

    [Fact]
    public async Task CallbackRequiresInitiatingBrowserCookie()
    {
        await using var app = await StartServerAsync();
        var registration = await RegisterClientAsync();
        var authorization = await BeginAuthorizationAsync(registration.ClientId);

        using var clientWithoutCookies = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectCallback = SocketsHttpHandler.ConnectCallback,
        })
        {
            BaseAddress = HttpClient.BaseAddress,
        };
        using var response = await clientWithoutCookies.GetAsync(
            $"/oauth/callback?code=upstream-code&state={Uri.EscapeDataString(authorization.TransactionId)}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_upstream.TokenRequests);
    }

    private async Task<WebApplication> StartServerAsync()
    {
        var app = Builder.Build();
        app.UseAuthorization();
        app.MapMcpOAuthProxy();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private async Task<ClientRegistration> RegisterClientAsync(
        string scope = "mcp:tools",
        bool includeRefreshGrant = true)
    {
        using var response = await HttpClient.PostAsJsonAsync(
            "/oauth/register",
            new OAuthProxyRegistrationTestRequest
            {
                RedirectUris = [ClientRedirectUri],
                TokenEndpointAuthMethod = "client_secret_post",
                GrantTypes = includeRefreshGrant
                    ? new[] { "authorization_code", "refresh_token" }
                    : new[] { "authorization_code" },
                ResponseTypes = ["code"],
                Scope = scope,
                ClientName = "Test MCP Client",
                ApplicationType = "native",
            },
            OAuthProxyTestJsonContext.Default.OAuthProxyRegistrationTestRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        Assert.Equal("none", document.RootElement.GetProperty("token_endpoint_auth_method").GetString());
        Assert.Equal(scope, document.RootElement.GetProperty("scope").GetString());
        Assert.Equal("Test MCP Client", document.RootElement.GetProperty("client_name").GetString());
        Assert.Equal("native", document.RootElement.GetProperty("application_type").GetString());
        return new(document.RootElement.GetProperty("client_id").GetString()!);
    }

    private async Task<AuthorizationStart> BeginAuthorizationAsync(string clientId, string scope = "mcp:tools")
    {
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var url = QueryHelpers.AddQueryString(
            "/oauth/authorize",
            new Dictionary<string, string?>
            {
                ["client_id"] = clientId,
                ["redirect_uri"] = ClientRedirectUri,
                ["response_type"] = "code",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["scope"] = scope,
                ["state"] = "client-state",
                ["resource"] = Resource,
            });

        using var response = await HttpClient.GetAsync(url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal("/upstream/authorize", location.AbsolutePath);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("S256", query["code_challenge_method"]);
        return new(
            verifier,
            challenge,
            query["code_challenge"].ToString(),
            query["state"].ToString(),
            query["resource"].ToString());
    }

    private async Task<string> CompleteCallbackAsync(string transactionId)
    {
        using var response = await HttpClient.GetAsync(
            $"/oauth/callback?code=upstream-code&state={Uri.EscapeDataString(transactionId)}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal(ClientRedirectUri, location.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("client-state", query["state"]);
        Assert.Equal("http://localhost:5000/oauth", query["iss"]);
        return query["code"].ToString();
    }

    private async Task<TokenResult> ExchangeCodeAsync(string clientId, string code, string verifier)
    {
        using var response = await PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = clientId,
            ["redirect_uri"] = ClientRedirectUri,
            ["code_verifier"] = verifier,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadTokenAsync(response);
    }

    private async Task<TokenResult> RefreshAsync(string clientId, string refreshToken)
    {
        using var response = await PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadTokenAsync(response);
    }

    private Task<HttpResponseMessage> PostTokenAsync(Dictionary<string, string> parameters) =>
        HttpClient.PostAsync(
            "/oauth/token",
            new FormUrlEncodedContent(parameters),
            TestContext.Current.CancellationToken);

    private static async Task<TokenResult> ReadTokenAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        var root = document.RootElement;
        return new(
            root.GetProperty("access_token").GetString()!,
            root.TryGetProperty("refresh_token", out var refreshToken) ? refreshToken.GetString() : null,
            response.Headers.CacheControl?.ToString());
    }

    private sealed record ClientRegistration(string ClientId);

    private sealed record AuthorizationStart(
        string CodeVerifier,
        string DownstreamCodeChallenge,
        string UpstreamCodeChallenge,
        string TransactionId,
        string UpstreamResource);

    private sealed record TokenResult(string AccessToken, string? RefreshToken, string? CacheControl);

    private sealed class RecordingStore : IMcpOAuthProxyStore
    {
        private readonly ConcurrentDictionary<string, ReadOnlyMemory<byte>> _values = new(StringComparer.Ordinal);

        public IEnumerable<ReadOnlyMemory<byte>> Values => _values.Values;

        public IEnumerable<string> Keys => _values.Keys;

        public bool BlockNextRefreshTake { get; set; }

        public TaskCompletionSource<bool> RefreshTakeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> ReleaseRefreshTake { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask SetAsync(string key, ReadOnlyMemory<byte> value, TimeSpan lifetime, CancellationToken cancellationToken = default)
        {
            _values[key] = value.ToArray();
            return ValueTask.CompletedTask;
        }

        public ValueTask<ReadOnlyMemory<byte>?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_values.TryGetValue(key, out var value) ? (ReadOnlyMemory<byte>?)value : null);

        public async ValueTask<ReadOnlyMemory<byte>?> TakeAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            var result = _values.TryRemove(key, out var value) ? value : (ReadOnlyMemory<byte>?)null;
            if (BlockNextRefreshTake && key.Contains(":refresh:", StringComparison.Ordinal))
            {
                BlockNextRefreshTake = false;
                RefreshTakeStarted.TrySetResult(true);
                await ReleaseRefreshTake.Task.WaitAsync(cancellationToken);
            }

            return result;
        }

        public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.TryRemove(key, out _);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StubHttpClientFactory()
        {
            _client = new HttpClient(new StubHandler(HandleAsync));
        }

        public List<Dictionary<string, string>> TokenRequests { get; } = [];

        public int DiscoveryRequests { get; private set; }

        public bool FailNextRefreshRequest { get; set; }

        public bool BlockNextRefreshRequest { get; set; }

        public bool ThrowNextRefreshTimeout { get; set; }

        public HttpStatusCode? MalformedRefreshFailureStatus { get; set; }

        public TaskCompletionSource<bool> RefreshRequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> ReleaseRefreshRequest { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HttpClient CreateClient(string name) => _client;

        private async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("/upstream/.well-known/openid-configuration", request.RequestUri?.AbsolutePath);
                DiscoveryRequests++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {"issuer":"http://localhost:5000/upstream","authorization_endpoint":"http://localhost:5000/upstream/authorize","token_endpoint":"http://localhost:5000/upstream/token","code_challenge_methods_supported":["S256"]}
                        """,
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            Assert.Equal("/upstream/token", request.RequestUri?.AbsolutePath);
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var parsed = QueryHelpers.ParseQuery(body);
            var fields = parsed.ToDictionary(static pair => pair.Key, static pair => pair.Value.ToString(), StringComparer.Ordinal);
            TokenRequests.Add(fields);

            var isRefresh = fields["grant_type"] == "refresh_token";
            if (isRefresh && BlockNextRefreshRequest)
            {
                BlockNextRefreshRequest = false;
                RefreshRequestStarted.TrySetResult(true);
                await ReleaseRefreshRequest.Task.WaitAsync(cancellationToken);
            }

            if (isRefresh && ThrowNextRefreshTimeout)
            {
                ThrowNextRefreshTimeout = false;
                throw new TaskCanceledException("The simulated upstream request timed out.");
            }

            if (isRefresh && MalformedRefreshFailureStatus is HttpStatusCode statusCode)
            {
                MalformedRefreshFailureStatus = null;
                return new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent("not-json", Encoding.UTF8, "application/json"),
                };
            }

            if (isRefresh && FailNextRefreshRequest)
            {
                FailNextRefreshRequest = false;
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent(
                        """{"error":"temporarily_unavailable"}""",
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            var suffix = isRefresh ? "2" : "1";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"access_token":"upstream-access-{{suffix}}","refresh_token":"upstream-refresh-{{suffix}}","id_token":"upstream-id-{{suffix}}","token_type":"Bearer","expires_in":3600,"scope":"mcp:tools"}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}

internal sealed class OAuthProxyRegistrationTestRequest
{
    [JsonPropertyName("redirect_uris")]
    public required string[] RedirectUris { get; init; }

    [JsonPropertyName("token_endpoint_auth_method")]
    public string? TokenEndpointAuthMethod { get; init; }

    [JsonPropertyName("grant_types")]
    public string[]? GrantTypes { get; init; }

    [JsonPropertyName("response_types")]
    public string[]? ResponseTypes { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    [JsonPropertyName("client_name")]
    public string? ClientName { get; init; }

    [JsonPropertyName("application_type")]
    public string? ApplicationType { get; init; }
}

[JsonSerializable(typeof(OAuthProxyRegistrationTestRequest))]
internal sealed partial class OAuthProxyTestJsonContext : JsonSerializerContext;
