namespace ModelContextProtocol.Authentication;

/// <summary>
/// Defines an interface for providing authentication for requests.
/// This is the main extensibility point for authentication in MCP clients.
/// </summary>
public interface IMcpAuthorizationProvider
{
    /// <summary>
    /// Gets the authentication scheme to use with credentials from this provider.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Common values include "Bearer" for JWT tokens and "Basic" for username/password authentication.
    /// </para>
    /// </remarks>
    string AuthorizationScheme { get; }

    /// <summary>
    /// Gets an authentication token or credential for authenticating requests to a resource.
    /// </summary>
    /// <param name="resourceUri">The URI of the resource requiring authentication.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An authentication token string or null if no token could be obtained.</returns>
    Task<string?> GetCredentialAsync(Uri resourceUri, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Handles a 401 Unauthorized response from a resource.
    /// </summary>
    /// <param name="response">The HTTP response that contained the 401 status code.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True if the provider was able to handle the unauthorized response, otherwise false.</returns>
    Task<bool> HandleUnauthorizedResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken = default);
}