using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace ModelContextProtocol.Tests.Authentication;

public class ClientOAuthProviderBackchannelTests
{
    private static readonly Uri s_resourceEndpoint = new("https://resource.example.com/mcp");
    private static readonly Uri s_resourceMetadataEndpoint = new("https://resource.example.com/.well-known/oauth-protected-resource");
    private static readonly Uri s_authorizationServer = new("https://auth.example.com");
    private static readonly Uri s_authorizationServerMetadataEndpoint = new("https://auth.example.com/.well-known/oauth-authorization-server");
    private static readonly Uri s_registrationEndpoint = new("https://auth.example.com/register");
    private static readonly Uri s_tokenEndpoint = new("https://auth.example.com/token");

    [Fact]
    public async Task ExplicitBackchannel_HandlesAuthorizationServerTraffic_AndIsNotDisposed()
    {
        List<Uri> resourceRequests = [];
        List<Uri> backchannelRequests = [];
        List<string> tokenGrantTypes = [];
        List<string?> resourceAccessTokens = [];

        using var resourceHandler = new TrackingHttpMessageHandler(request =>
        {
            resourceRequests.Add(request.RequestUri!);

            if (request.RequestUri == s_resourceEndpoint)
            {
                resourceAccessTokens.Add(request.Headers.Authorization?.Parameter);
                return Task.FromResult(request.Headers.Authorization is null
                    ? CreateChallengeResponse()
                    : CreateAcceptedResponse());
            }

            if (request.RequestUri == s_resourceMetadataEndpoint)
            {
                return Task.FromResult(CreateJsonResponse(
                    $$"""{"resource":"{{s_resourceEndpoint}}","authorization_servers":["{{s_authorizationServer}}"]}"""));
            }

            throw new InvalidOperationException($"Unexpected resource request: {request.Method} {request.RequestUri}");
        });
        using var resourceClient = new HttpClient(resourceHandler);

        using var backchannelHandler = new TrackingHttpMessageHandler(async request =>
        {
            backchannelRequests.Add(request.RequestUri!);

            if (request.RequestUri == s_authorizationServerMetadataEndpoint)
            {
                return CreateAuthorizationServerMetadataResponse();
            }

            if (request.RequestUri == s_registrationEndpoint)
            {
                return CreateJsonResponse(
                    """{"client_id":"dynamic-client","client_secret":"dynamic-secret","token_endpoint_auth_method":"client_secret_post"}""");
            }

            if (request.RequestUri == s_tokenEndpoint)
            {
                var form = await request.Content!.ReadAsStringAsync();
                var grantType = form.Contains("grant_type=refresh_token", StringComparison.Ordinal)
                    ? "refresh_token"
                    : "authorization_code";
                tokenGrantTypes.Add(grantType);

                return grantType == "refresh_token"
                    ? CreateJsonResponse("""{"access_token":"refreshed-token","token_type":"Bearer","expires_in":3600}""")
                    : CreateJsonResponse("""{"access_token":"initial-token","refresh_token":"refresh-token","token_type":"Bearer","expires_in":0}""");
            }

            throw new InvalidOperationException($"Unexpected backchannel request: {request.Method} {request.RequestUri}");
        });
        using var backchannel = new HttpClient(backchannelHandler);

        var options = CreateTransportOptions(backchannel);
        await using (var transport = new HttpClientTransport(options, resourceClient))
        {
            await using var session = await transport.ConnectAsync(TestContext.Current.CancellationToken);
            await session.SendMessageAsync(
                new JsonRpcNotification { Method = "notifications/first" },
                TestContext.Current.CancellationToken);
            await session.SendMessageAsync(
                new JsonRpcNotification { Method = "notifications/second" },
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(
            [s_resourceEndpoint, s_resourceMetadataEndpoint, s_resourceEndpoint, s_resourceEndpoint],
            resourceRequests);
        Assert.Equal(
            [s_authorizationServerMetadataEndpoint, s_registrationEndpoint, s_tokenEndpoint, s_tokenEndpoint],
            backchannelRequests);
        Assert.Equal(["authorization_code", "refresh_token"], tokenGrantTypes);
        Assert.Equal([null, "initial-token", "refreshed-token"], resourceAccessTokens);
        Assert.False(backchannelHandler.IsDisposed);
    }

    [Fact]
    public async Task NullBackchannel_UsesResourceClientForAllOAuthTraffic()
    {
        List<Uri> requests = [];

        using var handler = new TrackingHttpMessageHandler(async request =>
        {
            requests.Add(request.RequestUri!);

            if (request.RequestUri == s_resourceEndpoint)
            {
                return request.Headers.Authorization is null
                    ? CreateChallengeResponse()
                    : CreateAcceptedResponse();
            }

            if (request.RequestUri == s_resourceMetadataEndpoint)
            {
                return CreateJsonResponse(
                    $$"""{"resource":"{{s_resourceEndpoint}}","authorization_servers":["{{s_authorizationServer}}"]}""");
            }

            if (request.RequestUri == s_authorizationServerMetadataEndpoint)
            {
                return CreateAuthorizationServerMetadataResponse();
            }

            if (request.RequestUri == s_registrationEndpoint)
            {
                return CreateJsonResponse(
                    """{"client_id":"dynamic-client","client_secret":"dynamic-secret","token_endpoint_auth_method":"client_secret_post"}""");
            }

            if (request.RequestUri == s_tokenEndpoint)
            {
                _ = await request.Content!.ReadAsStringAsync();
                return CreateJsonResponse("""{"access_token":"access-token","token_type":"Bearer","expires_in":3600}""");
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        using var httpClient = new HttpClient(handler);

        var options = CreateTransportOptions(backchannel: null);
        await using var transport = new HttpClientTransport(options, httpClient);
        await using var session = await transport.ConnectAsync(TestContext.Current.CancellationToken);
        await session.SendMessageAsync(
            new JsonRpcNotification { Method = "notifications/test" },
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                s_resourceEndpoint,
                s_resourceMetadataEndpoint,
                s_authorizationServerMetadataEndpoint,
                s_registrationEndpoint,
                s_tokenEndpoint,
                s_resourceEndpoint,
            ],
            requests);
    }

    private static HttpClientTransportOptions CreateTransportOptions(HttpClient? backchannel)
        => new()
        {
            Endpoint = s_resourceEndpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            EnableStandaloneGetStream = false,
            OAuth = new ClientOAuthOptions
            {
                RedirectUri = new Uri("https://client.example.com/callback"),
                Backchannel = backchannel,
                AuthorizationCallbackHandler = (context, _) => Task.FromResult<AuthorizationResult?>(new()
                {
                    Code = "authorization-code",
                    State = GetQueryParameter(context.AuthorizationUri, "state"),
                }),
            },
        };

    private static HttpResponseMessage CreateChallengeResponse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Add(
            new AuthenticationHeaderValue("Bearer", $"resource_metadata=\"{s_resourceMetadataEndpoint}\""));
        return response;
    }

    private static HttpResponseMessage CreateAcceptedResponse()
        => new(HttpStatusCode.Accepted)
        {
            Content = new ByteArrayContent(Array.Empty<byte>()),
        };

    private static HttpResponseMessage CreateAuthorizationServerMetadataResponse()
        => CreateJsonResponse(
            $$"""
            {
              "issuer": "{{s_authorizationServer}}",
              "authorization_endpoint": "{{s_authorizationServer}}/authorize",
              "token_endpoint": "{{s_tokenEndpoint}}",
              "registration_endpoint": "{{s_registrationEndpoint}}",
              "code_challenge_methods_supported": ["S256"]
            }
            """);

    private static HttpResponseMessage CreateJsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static string? GetQueryParameter(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&'))
        {
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex > 0 &&
                string.Equals(pair.Substring(0, separatorIndex), name, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(pair.Substring(separatorIndex + 1).Replace("+", " "));
            }
        }

        return null;
    }

    private sealed class TrackingHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        public bool IsDisposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return handler(request);
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
