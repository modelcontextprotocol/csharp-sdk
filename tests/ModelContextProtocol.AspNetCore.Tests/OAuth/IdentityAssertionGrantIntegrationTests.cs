using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using System.Net.Http.Headers;

namespace ModelContextProtocol.AspNetCore.Tests.OAuth;

/// <summary>
/// Integration tests for Cross-Application Access authorization using the in-memory
/// test OAuth server as a stand-in for both the enterprise Identity Provider (IdP) and
/// the MCP Authorization Server (AS).
///
/// Flow exercised:
///   1. <see cref="IdentityAssertionGrantProvider.GetAccessTokenAsync"/> discovers the MCP AS
///      metadata and calls the ID token callback.
///   2. The provider performs RFC 8693 token exchange at <c>/idp/token</c> on the test OAuth server
///      (ID token → JAG).
///   3. The provider exchanges the JAG for an access token at <c>/token</c>
///      (RFC 7523 JWT-bearer grant: JAG → access token).
///   4. The access token is passed to the MCP client transport and used to authenticate
///      against the protected MCP server.
/// </summary>
public class IdentityAssertionGrantIntegrationTests : OAuthTestBase
{
    public IdentityAssertionGrantIntegrationTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    [Fact]
    public async Task CanAuthenticate_WithIdentityAssertionGrantProvider()
    {
        // Enable Enterprise Managed Authorization endpoints on the test OAuth server.
        TestOAuthServer.EnterpriseSupportEnabled = true;

        await using var app = await StartMcpServerAsync();

        // Simulate the enterprise ID token that would normally come from the SSO login step.
        const string simulatedIdToken = "test-enterprise-sso-id-token";

        // Create the provider with IdP config folded into options.
        // The ID token callback just returns the SSO ID token; the provider performs
        // RFC 8693 (ID token → JAG) and RFC 7523 (JAG → access token) internally.
        var provider = new IdentityAssertionGrantProvider(
            new IdentityAssertionGrantProviderOptions
            {
                ClientId = "enterprise-mcp-client",
                ClientSecret = "enterprise-mcp-secret",
                IdpTokenEndpoint = $"{OAuthServerUrl}/idp/token",
                IdpClientId = "enterprise-idp-client",
                IdpClientSecret = "enterprise-idp-secret",
                IdTokenCallback = (_, ct) => Task.FromResult(simulatedIdToken),
            },
            httpClient: HttpClient);

        // Run the full Cross-Application Access flow: discover AS → get JAG → exchange for access token.
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
    public async Task IdentityAssertionGrantProvider_ReturnsCachedToken_OnSecondCall()
    {
        TestOAuthServer.EnterpriseSupportEnabled = true;

        await using var _ = await StartMcpServerAsync();

        var idTokenCallCount = 0;

        var provider = new IdentityAssertionGrantProvider(
            new IdentityAssertionGrantProviderOptions
            {
                ClientId = "enterprise-mcp-client",
                ClientSecret = "enterprise-mcp-secret",
                IdpTokenEndpoint = $"{OAuthServerUrl}/idp/token",
                IdpClientId = "enterprise-idp-client",
                IdpClientSecret = "enterprise-idp-secret",
                IdTokenCallback = (_, ct) =>
                {
                    idTokenCallCount++;
                    return Task.FromResult("test-sso-token");
                },
            },
            httpClient: HttpClient);

        var tokens1 = await provider.GetAccessTokenAsync(
            new Uri(McpServerUrl), new Uri(OAuthServerUrl),
            TestContext.Current.CancellationToken);

        var tokens2 = await provider.GetAccessTokenAsync(
            new Uri(McpServerUrl), new Uri(OAuthServerUrl),
            TestContext.Current.CancellationToken);

        // The ID token callback (and therefore the IdP round-trip) should only fire once.
        Assert.Equal(1, idTokenCallCount);
        Assert.Equal(tokens1.AccessToken, tokens2.AccessToken);
    }

    [Fact]
    public async Task IdentityAssertionGrantProvider_FetchesFreshToken_AfterInvalidateCache()
    {
        TestOAuthServer.EnterpriseSupportEnabled = true;

        await using var _ = await StartMcpServerAsync();

        var idTokenCallCount2 = 0;

        var provider2 = new IdentityAssertionGrantProvider(
            new IdentityAssertionGrantProviderOptions
            {
                ClientId = "enterprise-mcp-client",
                ClientSecret = "enterprise-mcp-secret",
                IdpTokenEndpoint = $"{OAuthServerUrl}/idp/token",
                IdpClientId = "enterprise-idp-client",
                IdpClientSecret = "enterprise-idp-secret",
                IdTokenCallback = (_, ct) =>
                {
                    idTokenCallCount2++;
                    return Task.FromResult("test-sso-token");
                },
            },
            httpClient: HttpClient);

        var tokens1 = await provider2.GetAccessTokenAsync(
            new Uri(McpServerUrl), new Uri(OAuthServerUrl),
            TestContext.Current.CancellationToken);

        // Invalidate the cache to force a full re-exchange.
        provider2.InvalidateCache();

        var tokens2 = await provider2.GetAccessTokenAsync(
            new Uri(McpServerUrl), new Uri(OAuthServerUrl),
            TestContext.Current.CancellationToken);

        // The IdP should have been called twice — once for each GetAccessTokenAsync after invalidation.
        Assert.Equal(2, idTokenCallCount2);
        // The tokens may or may not be identical depending on timing, but the flow ran again.
        Assert.NotNull(tokens2.AccessToken);
    }
}
