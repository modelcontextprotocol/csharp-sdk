namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents a method that returns a subject token for use in a Cross-Application Access authorization flow.
/// </summary>
/// <param name="context">
/// Context containing the MCP resource and authorization server URLs discovered during the OAuth flow.
/// </param>
/// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
/// <returns>
/// A task that represents the asynchronous operation. The task result contains the subject token obtained
/// from the enterprise Identity Provider (e.g., via SSO login). This is an OIDC ID token by default, or a
/// SAML 2.0 assertion when configured through <see cref="IdentityAssertionGrantProviderOptions.SubjectTokenType"/>.
/// The provider uses the token to perform the RFC 8693 token exchange and obtain a JWT Authorization Grant.
/// </returns>
public delegate Task<string> IdentityAssertionGrantIdTokenCallback(
    IdentityAssertionGrantContext context,
    CancellationToken cancellationToken);
