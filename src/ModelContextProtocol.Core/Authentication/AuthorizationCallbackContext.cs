namespace ModelContextProtocol.Authentication;

/// <summary>
/// Provides the information needed to complete an OAuth authorization request.
/// </summary>
public sealed class AuthorizationCallbackContext
{
    /// <summary>
    /// Gets the authorization URI that the user needs to visit.
    /// </summary>
    public required Uri AuthorizationUri { get; init; }

    /// <summary>
    /// Gets the redirect URI where the authorization response will be sent.
    /// </summary>
    public required Uri RedirectUri { get; init; }
}
