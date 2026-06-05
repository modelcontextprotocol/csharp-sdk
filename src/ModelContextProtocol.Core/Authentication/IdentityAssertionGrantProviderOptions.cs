namespace ModelContextProtocol.Authentication;

/// <summary>
/// Configuration options for the <see cref="IdentityAssertionGrantProvider"/>.
/// </summary>
public sealed class IdentityAssertionGrantProviderOptions
{
    /// <summary>
    /// Gets or sets the MCP client ID used for the JWT Bearer grant (RFC 7523) at the MCP authorization server.
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// Gets or sets the MCP client secret used for the JWT Bearer grant at the MCP authorization server.
    /// Optional; only required if the MCP authorization server requires client authentication.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the scopes to request from the MCP authorization server (space-separated). Optional.
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Gets or sets the enterprise Identity Provider base URL for OAuth/OIDC metadata discovery.
    /// Used to discover <c>IdpTokenEndpoint</c> automatically when <see cref="IdpTokenEndpoint"/> is not set.
    /// Either this or <see cref="IdpTokenEndpoint"/> must be provided.
    /// </summary>
    public string? IdpUrl { get; set; }

    /// <summary>
    /// Gets or sets the enterprise Identity Provider token endpoint URL for RFC 8693 token exchange.
    /// When provided, skips IdP metadata discovery. Either this or <see cref="IdpUrl"/> must be provided.
    /// </summary>
    public string? IdpTokenEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the client ID for authentication with the enterprise Identity Provider (RFC 8693 token exchange).
    /// </summary>
    public required string IdpClientId { get; set; }

    /// <summary>
    /// Gets or sets the client secret for authentication with the enterprise Identity Provider. Optional.
    /// </summary>
    public string? IdpClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the scopes to request from the enterprise Identity Provider (space-separated). Optional.
    /// </summary>
    public string? IdpScope { get; set; }

    /// <summary>
    /// Gets or sets the callback that supplies the OIDC ID token for the Cross-Application Access flow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This callback is invoked after the MCP resource and authorization server URLs have been discovered.
    /// It receives a <see cref="IdentityAssertionGrantContext"/> with these URLs and should return the
    /// OIDC ID token string obtained from the enterprise Identity Provider (e.g., from an SSO login session).
    /// </para>
    /// <para>
    /// The provider will use the returned ID token to internally perform the RFC 8693 token exchange at the
    /// configured IdP, obtaining a JWT Authorization Grant, which is then exchanged for an access token at
    /// the MCP authorization server via RFC 7523.
    /// </para>
    /// </remarks>
    public required IdentityAssertionGrantIdTokenCallback IdTokenCallback { get; set; }
}
