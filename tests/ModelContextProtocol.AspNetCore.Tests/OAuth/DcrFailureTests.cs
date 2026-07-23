using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace ModelContextProtocol.AspNetCore.Tests.OAuth;

// SEP-837: the SDK doesn't surface or retry DCR failures itself, but a consumer built on the SDK
// must be able to. These tests prove that surface: a rejected registration propagates with enough
// context to build a meaningful error, and a consumer can retry with an adjusted redirect URI.
public class DcrFailureTests : OAuthTestBase
{
    public DcrFailureTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    [Fact]
    public async Task DcrRejection_PropagatesToConsumer_WithStatusBodyAndSentParameters()
    {
        await using var app = await StartMcpServerAsync();

        // A custom-scheme redirect URI infers application_type "native"; the OIDC AS rejects it
        // with 400 invalid_redirect_uri because it only registers http/https redirect URIs.
        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new ClientOAuthOptions()
            {
                RedirectUri = new Uri("myapp://callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
                DynamicClientRegistration = new() { ClientName = "Test MCP Client" },
            },
        }, HttpClient, LoggerFactory);

        var ex = await Assert.ThrowsAsync<McpException>(() => McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        // The consumer needs enough to produce a meaningful error: the HTTP status, the AS error
        // body (which echoes the redirect URI), and the application_type the SDK actually sent.
        Assert.Contains("BadRequest", ex.Message);
        Assert.Contains("invalid_redirect_uri", ex.Message);
        Assert.Contains("native", ex.Message);
    }

    [Fact]
    public async Task ConsumerCanRetryRegistration_WithAdjustedRedirectUri_AfterRejection()
    {
        await using var app = await StartMcpServerAsync();

        // First attempt: a custom-scheme redirect (native) is rejected by the AS. ApplicationType
        // is held constant at "native" so only the redirect URI changes between the two attempts.
        await using var firstTransport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new ClientOAuthOptions()
            {
                RedirectUri = new Uri("myapp://callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
                DynamicClientRegistration = new()
                {
                    ClientName = "Test MCP Client",
                    ApplicationType = "native",
                },
            },
        }, HttpClient, LoggerFactory);

        await Assert.ThrowsAsync<McpException>(() => McpClient.CreateAsync(
            firstTransport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        // Second attempt: a new provider on the SAME HttpClient with an adjusted (loopback) redirect
        // URI that the AS accepts. The retry must succeed, proving the SEP-837 MAY-retry surface works
        // and that the rejected attempt left no client state behind.
        await using var secondTransport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new ClientOAuthOptions()
            {
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
                DynamicClientRegistration = new()
                {
                    ClientName = "Test MCP Client",
                    ApplicationType = "native",
                },
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            secondTransport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("native", TestOAuthServer.LastApplicationType);
    }
}
