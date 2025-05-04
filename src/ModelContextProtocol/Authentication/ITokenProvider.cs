namespace ModelContextProtocol.Authentication;

/// <summary>
/// Defines an interface for providing authentication for requests.
/// This is the main extensibility point for authentication in MCP clients.
/// </summary>
public interface ITokenProvider
{
    /// <summary>
    /// Gets an authentication token or credential for authenticating requests to a resource.
    /// </summary>
    /// <param name="resourceUri">The URI of the resource requiring authentication.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An authentication token string or null if no token could be obtained.</returns>
    Task<string?> GetTokenAsync(Uri resourceUri, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Handles a 401 Unauthorized response from a resource.
    /// </summary>
    /// <param name="response">The HTTP response that contained the 401 status code.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True if the provider was able to handle the unauthorized response, otherwise false.</returns>
    Task<bool> HandleUnauthorizedResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken = default);
}