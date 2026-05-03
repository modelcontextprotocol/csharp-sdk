using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using System.Net.Http.Headers;

namespace ModelContextProtocol.AspNetCore.Tests.OAuth;

/// <summary>
/// Integration tests for Enterprise Managed Authorization (SEP-990) using the in-memory
/// test OAuth server as a stand-in for both the enterprise Identity Provider (IdP) and
/// the MCP Authorization Server (AS).
///
/// Flow exercised:
///   1. <see cref="EnterpriseAuthProvider.GetAccessTokenAsync"/> discovers the MCP AS
///      metadata and calls the assertion callback.
///   2. The assertion callback calls <c>/idp/token</c> on the test OAuth server
///      (RFC 8693 token exchange: ID token → JAG).
///   3. The provider exchanges the JAG for an access token at <c>/token</c>
///      (RFC 7523 JWT-bearer grant: JAG → access token).
///   4. The access token is passed to the MCP client transport and used to authenticate
///      against the protected MCP server.
/// </summary>
public class EnterpriseAuthIntegrationTests : OAuthTestBase
{
    public EnterpriseAuthIntegrationTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    [Fact]
    public async Task CanAuthenticate_WithEnterpriseAuthProvider()
    {
        // Enable SEP-990 endpoints on the test OAuth server.
        TestOAuthServer.EnterpriseSupportEnabled = true;

        await using var app = await StartMcpServerAsync();

        // Simulate the enterprise ID token that would normally come from the SSO login step.
        const string simulatedIdToken = "test-enterprise-sso-id-token";

        // Create the provider.  The assertion callback calls the IdP's token-exchange
        // endpoint (/idp/token on the test OAuth server) to obtain a JAG, which is then
        // exchanged automatically for an access token at the MCP AS token endpoint (/token).
        var provider = new EnterpriseAuthProvider(
            new EnterpriseAuthProviderOptions
            {
                ClientId = "enterprise-mcp-client",
                ClientSecret = "enterprise-mcp-secret",
                AssertionCallback = (context, ct) =>
                    EnterpriseAuth.RequestJwtAuthorizationGrantAsync(
                        new RequestJwtAuthGrantOptions
                        {
                            // /idp/token acts as the enterprise IdP token endpoint.
                            TokenEndpoint = $"{OAuthServerUrl}/idp/token",
                            // The JAG audience is the MCP AS, and the resource is the MCP server.
                            Audience = context.AuthorizationServerUrl.ToString(),
                            Resource = context.ResourceUrl.ToString(),
                            IdToken = simulatedIdToken,
                            ClientId = "enterprise-idp-client",
                            ClientSecret = "enterprise-idp-secret",
                            HttpClient = HttpClient,
                        }, ct),
            },
            httpClient: HttpClient);

        // Run the full SEP-990 flow: discover AS → get JAG → exchange for access token.
        var tokens = await provider.GetAccessTokenAsync(
            resourceUrl: new Uri(McpServerUrl),
            authorizationServerUrl: new Uri(OAuthServerUrl),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(tokens.AccessToken);
        Assert.False(string.IsNullOrEmpty(tokens.AccessToken));
        Assert.Equal("bearer", tokens.TokenType, ignoreCase: true);

        // Wire the obtained access token into an HTTP client that shares the same
        // in-memory Kestrel transport as the rest of the test fixture.
        var mcpHttpClient = new HttpClient(SocketsHttpHandler, disposeHandler: false);
        ConfigureHttpClient(mcpHttpClient);
        mcpHttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        // Connect the MCP client using the enterprise access token — no interactive OAuth flow.
        await using var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(McpServerUrl) },
            mcpHttpClient,
            LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        // If we get here the MCP server accepted the enterprise access token.
        Assert.NotNull(client);
    }

    [Fact]
    public async Task EnterpriseAuthProvider_ReturnsCachedToken_OnSecondCall()
    {
        TestOAuthServer.EnterpriseSupportEnabled = true;

        await using var _ = await StartMcpServerAsync();

        var assertionCallCount = 0;

        var provider = new EnterpriseAuthProvider(
            new EnterpriseAuthProviderOptions
            {
                ClientId = "enterprise-mcp-client",
                ClientSecret = "enterprise-mcp-secret",
                AssertionCallback = async (context, ct) =>
                {
                    assertionCallCount++;
                    return await EnterpriseAuth.RequestJwtAuthorizationGrantAsync(
                        new RequestJwtAuthGrantOptions
                        {
                            TokenEndpoint = $"{OAuthServerUrl}/idp/token",
                            Audience = context.AuthorizationServerUrl.ToString(),
                            Resource = context.ResourceUrl.ToString(),
                            IdToken = "test-sso-token",
                            ClientId = "enterprise-idp-client",
                            ClientSecret = "enterprise-idp-secret",
                            HttpClient = HttpClient,
                        }, ct);
                },
            },
            httpClient: HttpClient);

        var tokens1 = await provider.GetAccessTokenAsync(
            new Uri(McpServerUrl), new Uri(OAuthServerUrl),
            TestContext.Current.CancellationToken);

        var tokens2 = await provider.GetAccessTokenAsync(
            new Uri(McpServerUrl), new Uri(OAuthServerUrl),
            TestContext.Current.CancellationToken);

        // The assertion callback (and therefore the IdP round-trip) should only fire once.
        Assert.Equal(1, assertionCallCount);
        Assert.Equal(tokens1.AccessToken, tokens2.AccessToken);
    }

    [Fact]
    public async Task EnterpriseAuthProvider_FetchesFreshToken_AfterInvalidateCache()
    {
        TestOAuthServer.EnterpriseSupportEnabled = true;

        await using var _ = await StartMcpServerAsync();

        var assertionCallCount = 0;

        var provider = new EnterpriseAuthProvider(
            new EnterpriseAuthProviderOptions
            {
                ClientId = "enterprise-mcp-client",
                ClientSecret = "enterprise-mcp-secret",
                AssertionCallback = async (context, ct) =>
                {
                    assertionCallCount++;
                    return await EnterpriseAuth.RequestJwtAuthorizationGrantAsync(
                        new RequestJwtAuthGrantOptions
                        {
                            TokenEndpoint = $"{OAuthServerUrl}/idp/token",
                            Audience = context.AuthorizationServerUrl.ToString(),
                            Resource = context.ResourceUrl.ToString(),
                            IdToken = "test-sso-token",
                            ClientId = "enterprise-idp-client",
                            ClientSecret = "enterprise-idp-secret",
                            HttpClient = HttpClient,
                        }, ct);
                },
            },
            httpClient: HttpClient);

        var tokens1 = await provider.GetAccessTokenAsync(
            new Uri(McpServerUrl), new Uri(OAuthServerUrl),
            TestContext.Current.CancellationToken);

        // Invalidate the cache to force a full re-exchange.
        provider.InvalidateCache();

        var tokens2 = await provider.GetAccessTokenAsync(
            new Uri(McpServerUrl), new Uri(OAuthServerUrl),
            TestContext.Current.CancellationToken);

        // The IdP should have been called twice — once for each GetAccessTokenAsync after invalidation.
        Assert.Equal(2, assertionCallCount);
        // The tokens may or may not be identical depending on timing, but the flow ran again.
        Assert.NotNull(tokens2.AccessToken);
    }
}
