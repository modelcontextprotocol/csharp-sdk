using System.Net.Http.Headers;
using System.Collections.Concurrent;
using ModelContextProtocol.Auth.Types;

namespace ModelContextProtocol.Auth;

/// <summary>
/// Provides extension methods for MCP clients to handle authentication.
/// </summary>
public static class McpClientExtensions
{
    // Store client configuration data in a static dictionary
    private static readonly ConcurrentDictionary<HttpClient, AuthorizationConfig> _clientConfigs = new();
    
    /// <summary>
    /// Attaches an OAuth token to the HTTP request.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="token">The OAuth token.</param>
    public static void AttachToken(this HttpClient httpClient, string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new ArgumentNullException(nameof(token));
        }

        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Configures a client to handle authorization challenges automatically.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="redirectUri">The URI to redirect to after authentication.</param>
    /// <param name="clientId">The client ID to use for authentication, or null to register a new client.</param>
    /// <param name="clientName">The client name to use for registration.</param>
    /// <param name="scopes">The requested scopes.</param>
    /// <param name="handler">The handler to invoke when authorization is required.</param>
    public static void ConfigureAuthorizationHandler(
        this HttpClient httpClient,
        Uri redirectUri,
        string? clientId = null,
        string? clientName = null,
        IEnumerable<string>? scopes = null,
        Func<Uri, Task<string>>? handler = null)
    {
        // Store authorization parameters for the HttpClient
        var config = new AuthorizationConfig
        {
            RedirectUri = redirectUri,
            ClientId = clientId,
            ClientName = clientName,
            Scopes = scopes?.ToList(),
            AuthorizationHandler = handler
        };
        
        _clientConfigs[httpClient] = config;
    }
    
    /// <summary>
    /// Gets the authorization configuration for the HTTP client.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <returns>The authorization configuration, or null if not configured.</returns>
    public static AuthorizationConfig? GetAuthorizationConfig(this HttpClient httpClient)
    {
        _clientConfigs.TryGetValue(httpClient, out var config);
        return config;
    }
    
    /// <summary>
    /// Handles a 401 Unauthorized response from an MCP server.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="response">The HTTP response with the 401 status code.</param>
    /// <returns>The OAuth token response if authentication was successful.</returns>
    public static async Task<OAuthToken> HandleUnauthorizedResponseAsync(
        this HttpClient httpClient,
        HttpResponseMessage response)
    {
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
        {
            throw new ArgumentException("The response status code must be 401 Unauthorized.", nameof(response));
        }
        
        // Get the WWW-Authenticate header
        var wwwAuthenticateHeader = response.Headers.WwwAuthenticate.ToString();
        if (string.IsNullOrEmpty(wwwAuthenticateHeader))
        {
            throw new InvalidOperationException("The response does not contain a WWW-Authenticate header.");
        }
        
        // Get the authorization configuration
        var config = httpClient.GetAuthorizationConfig();
        if (config == null)
        {
            throw new InvalidOperationException("The HTTP client has not been configured for authorization handling. Call ConfigureAuthorizationHandler() first.");
        }
        
        // Create OAuthAuthenticationService - use appropriate constructor based on whether we have a handler
        OAuthService authService = config.AuthorizationHandler != null
            ? new OAuthService(config.AuthorizationHandler)
            : new OAuthService();
        
        // Get resource URI
        var resourceUri = response.RequestMessage?.RequestUri ?? throw new InvalidOperationException("Request URI is not available.");
        
        // Start the authentication flow
        var tokenResponse = await authService.HandleAuthenticationAsync(
            resourceUri,
            wwwAuthenticateHeader,
            config.RedirectUri,
            config.ClientId,
            config.ClientName,
            config.Scopes);
        
        // Attach the access token to future requests
        httpClient.AttachToken(tokenResponse.AccessToken);
        
        return tokenResponse;
    }
}
