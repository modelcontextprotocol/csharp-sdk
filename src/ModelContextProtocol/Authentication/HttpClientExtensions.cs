namespace ModelContextProtocol.Authentication;

/// <summary>
/// Extension methods for <see cref="HttpClient"/> related to authentication.
/// </summary>
public static class HttpClientExtensions
{
    /// <summary>
    /// Configures the <see cref="HttpClient"/> to use the specified access token provider for authentication.
    /// </summary>
    /// <param name="httpClient">The HTTP client to configure.</param>
    /// <param name="tokenProvider">The token provider that will supply authentication tokens.</param>
    /// <param name="scheme">The authentication scheme to use. Defaults to "Bearer".</param>
    /// <returns>A new <see cref="HttpClient"/> that automatically handles authentication.</returns>
    /// <remarks>
    /// This extension method configures the HttpClient with a handler that automatically:
    /// <list type="bullet">
    /// <item>Adds authentication tokens to outgoing requests</item>
    /// <item>Handles 401 Unauthorized responses by attempting to refresh tokens</item>
    /// <item>Retries the request with the new token if token refresh is successful</item>
    /// </list>
    /// </remarks>
    public static HttpClient UseMcpAuthorizationProvider(this HttpClient httpClient, IMcpAuthorizationProvider tokenProvider, string scheme = "Bearer")
    {
        if (httpClient == null)
            throw new ArgumentNullException(nameof(httpClient));

        if (tokenProvider == null)
            throw new ArgumentNullException(nameof(tokenProvider));

        if (string.IsNullOrWhiteSpace(scheme))
            throw new ArgumentException("Authentication scheme cannot be null or whitespace", nameof(scheme));

        // Create a new HttpClientHandler with the same settings as the current client
        var handler = new HttpClientHandler();
        if (httpClient.DefaultRequestHeaders != null && httpClient.DefaultRequestHeaders.Host != null)
        {
            // Copy relevant settings from the original client's handler if possible
            // This is a simplified approach - some settings might not be accessible
        }

        // Create our authentication delegating handler with the token provider
        var authHandler = new AuthorizationDelegatingHandler(tokenProvider, scheme)
        {
            InnerHandler = handler
        };

        // Create a new HttpClient with our delegating handler
        var newClient = new HttpClient(authHandler);

        // Copy settings from the original client
        newClient.BaseAddress = httpClient.BaseAddress;
        newClient.Timeout = httpClient.Timeout;
        newClient.MaxResponseContentBufferSize = httpClient.MaxResponseContentBufferSize;

        // Copy headers from original client to new client
        if (httpClient.DefaultRequestHeaders != null)
        {
            foreach (var header in httpClient.DefaultRequestHeaders)
            {
                newClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return newClient;
    }
}