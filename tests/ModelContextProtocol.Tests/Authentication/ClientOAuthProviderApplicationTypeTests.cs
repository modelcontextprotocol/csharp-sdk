using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace ModelContextProtocol.Tests.Authentication;

// ClientOAuthProvider is internal; construct it indirectly via HttpClientTransport
// to verify DCR application_type validation is deferred until DCR is selected.
public class ClientOAuthProviderApplicationTypeTests
{
    private static readonly Uri ServerEndpoint = new("https://server.example.com/mcp");

    private static HttpClientTransportOptions BuildOptions(string redirectUri, string? explicitApplicationType = null)
    {
        return new HttpClientTransportOptions
        {
            Endpoint = ServerEndpoint,
            OAuth = new ClientOAuthOptions
            {
                RedirectUri = new Uri(redirectUri),
                DynamicClientRegistration = new DynamicClientRegistrationOptions
                {
                    ApplicationType = explicitApplicationType,
                },
            },
        };
    }

    [Theory]
    [InlineData("http://localhost:8080/callback", "web")]
    [InlineData("https://example.com/callback", "native")]
    public void Constructor_Defers_ApplicationTypeValidation_UntilDynamicRegistration(
        string redirectUri, string explicitType)
    {
        var options = BuildOptions(redirectUri, explicitType);

        using var httpClient = new HttpClient();
        _ = new HttpClientTransport(options, httpClient);

        Assert.Equal(explicitType, options.OAuth!.DynamicClientRegistration!.ApplicationType);
    }
}
