// filepath: c:\Users\ddelimarsky\source\csharp-sdk-anm\src\ModelContextProtocol\Auth\McpClientExtensions.cs
using System.Net.Http.Headers;
using System.Collections.Concurrent;

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
    public static async Task<OAuthTokenResponse> HandleUnauthorizedResponseAsync(
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
        
        // Create OAuthAuthenticationService
        var authService = new OAuthAuthenticationService();
        
        // Get resource URI
        var resourceUri = response.RequestMessage?.RequestUri ?? throw new InvalidOperationException("Request URI is not available.");
        
        // Start the authentication flow
        try
        {
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
        catch (NotImplementedException ex) when (ex.Message.Contains("Authorization requires user interaction"))
        {
            // Extract the authorization URL from the exception message
            var authUrlStart = ex.Message.IndexOf("http");
            var authUrlEnd = ex.Message.IndexOf("\n", authUrlStart);
            var authUrl = ex.Message.Substring(authUrlStart, authUrlEnd - authUrlStart);
            
            // Check if a handler is registered
            if (config.AuthorizationHandler != null)
            {
                // Call the handler to get the authorization code
                var authCode = await config.AuthorizationHandler(new Uri(authUrl));
                
                // In a real implementation, we would use the authorization code to get a token
                // For now, throw an exception with instructions
                throw new NotImplementedException(
                    "Authorization code acquired, but token exchange is not implemented. " +
                    "In a real implementation, this would call ExchangeAuthorizationCodeForTokenAsync.");
            }
            else
            {
                // Re-throw the original exception
                throw;
            }
        }
    }
}

/// <summary>
/// Configuration for OAuth authorization.
/// </summary>
public class AuthorizationConfig
{
    /// <summary>
    /// The URI to redirect to after authentication.
    /// </summary>
    public Uri RedirectUri { get; set; } = null!;
    
    /// <summary>
    /// The client ID to use for authentication, or null to register a new client.
    /// </summary>
    public string? ClientId { get; set; }
    
    /// <summary>
    /// The client name to use for registration.
    /// </summary>
    public string? ClientName { get; set; }
    
    /// <summary>
    /// The requested scopes.
    /// </summary>
    public IEnumerable<string>? Scopes { get; set; }
    
    /// <summary>
    /// The handler to invoke when authorization is required.
    /// </summary>
    public Func<Uri, Task<string>>? AuthorizationHandler { get; set; }
}