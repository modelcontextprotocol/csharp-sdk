using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace ModelContextProtocol.Tests.Authentication;

// ClientOAuthProvider is internal; construct it indirectly via HttpClientTransport
// so we can observe the application_type value mutated back onto the options.
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
    [InlineData("http://localhost:8080/callback", "native")]
    [InlineData("http://127.0.0.1:8080/callback", "native")]
    [InlineData("http://[::1]:8080/callback", "native")]
    [InlineData("myapp://callback", "native")]
    [InlineData("https://example.com/callback", "web")]
    public void Constructor_Infers_ApplicationType_From_RedirectUri(string redirectUri, string expected)
    {
        var options = BuildOptions(redirectUri);

        using var httpClient = new HttpClient();
        _ = new HttpClientTransport(options, httpClient);

        Assert.Equal(expected, options.OAuth!.DynamicClientRegistration!.ApplicationType);
    }

    [Theory]
    [InlineData("http://localhost:8080/callback", "native")]
    [InlineData("https://example.com/callback", "web")]
    public void Constructor_Preserves_Explicit_ApplicationType_When_It_Matches_Inferred(string redirectUri, string explicitType)
    {
        var options = BuildOptions(redirectUri, explicitType);

        using var httpClient = new HttpClient();
        _ = new HttpClientTransport(options, httpClient);

        Assert.Equal(explicitType, options.OAuth!.DynamicClientRegistration!.ApplicationType);
    }

    [Theory]
    [InlineData("http://localhost:8080/callback", "web")]
    [InlineData("https://example.com/callback", "native")]
    public void Constructor_Throws_When_Explicit_ApplicationType_Conflicts_With_Inferred(
        string redirectUri, string explicitType)
    {
        var options = BuildOptions(redirectUri, explicitType);

        using var httpClient = new HttpClient();
        var ex = Assert.Throws<ArgumentException>(() => new HttpClientTransport(options, httpClient));

        Assert.Contains("conflicts with the type inferred from the redirect URI", ex.Message);
        Assert.Equal("options", ex.ParamName);
    }
}
