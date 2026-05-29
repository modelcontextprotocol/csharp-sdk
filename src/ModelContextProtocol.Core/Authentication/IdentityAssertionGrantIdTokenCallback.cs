namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents a method that returns an OIDC ID token for use in a Cross-Application Access authorization flow.
/// </summary>
/// <param name="context">
/// Context containing the MCP resource and authorization server URLs discovered during the OAuth flow.
/// </param>
/// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
/// <returns>
/// A task that represents the asynchronous operation. The task result contains the OIDC ID token string
/// obtained from the enterprise Identity Provider (e.g., via SSO login). The provider will then use this
/// ID token to perform the RFC 8693 token exchange to obtain a JWT Authorization Grant.
/// </returns>
public delegate Task<string> IdentityAssertionGrantIdTokenCallback(
    IdentityAssertionGrantContext context,
    CancellationToken cancellationToken);
