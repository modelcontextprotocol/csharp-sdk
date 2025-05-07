namespace ModelContextProtocol.Authentication;

/// <summary>
/// Defines an interface for providing authentication for requests.
/// This is the main extensibility point for authentication in MCP clients.
/// </summary>
public interface ITokenProvider
{
    /// <summary>
    /// Gets the collection of authentication schemes supported by this provider.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property returns all authentication schemes that this provider can handle,
    /// allowing clients to select the appropriate scheme based on server capabilities.
    /// </para>
    /// <para>
    /// Common values include "Bearer" for JWT tokens, "Basic" for username/password authentication,
    /// and "Negotiate" for integrated Windows authentication.
    /// </para>
    /// </remarks>
    IEnumerable<string> SupportedSchemes { get; }

    /// <summary>
    /// Gets an authentication token or credential for authenticating requests to a resource
    /// using the specified authentication scheme.
    /// </summary>
    /// <param name="scheme">The authentication scheme to use.</param>
    /// <param name="resourceUri">The URI of the resource requiring authentication.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An authentication token string or null if no token could be obtained for the specified scheme.</returns>
    Task<string?> GetCredentialAsync(string scheme, Uri resourceUri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles a 401 Unauthorized response from a resource.
    /// </summary>
    /// <param name="response">The HTTP response that contained the 401 status code.</param>
    /// <param name="scheme">The authentication scheme that was used when the unauthorized response was received.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A tuple containing a boolean indicating if the provider was able to handle the unauthorized response,
    /// and the authentication scheme that should be used for the next attempt.
    /// </returns>
    Task<(bool Success, string? RecommendedScheme)> HandleUnauthorizedResponseAsync(
        HttpResponseMessage response, 
        string scheme,
        CancellationToken cancellationToken = default);
}