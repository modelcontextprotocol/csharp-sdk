namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents the result of an OAuth authorization redirect, containing the authorization code
/// and state from the authorization response.
/// </summary>
/// <remarks>
/// The <see cref="State"/> property must be populated from the <c>state</c> query parameter in the
/// redirect URI. The SDK validates it against the value sent in the authorization request to bind
/// the response to the initiating transaction and mitigate cross-site request forgery attacks.
/// </remarks>
public sealed class AuthorizationResult
{
    /// <summary>
    /// Gets the authorization code returned by the authorization server.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    /// Gets the state value returned in the authorization response.
    /// </summary>
    /// <remarks>
    /// Implementations of <see cref="ClientOAuthOptions.AuthorizationCallbackHandler"/> must populate this
    /// property from the <c>state</c> query parameter of the redirect URI callback. The SDK requires an
    /// exact match with the state sent in the authorization request before exchanging the authorization code.
    /// </remarks>
    public string? State { get; init; }
}
