namespace ModelContextProtocol.Authentication;

/// <summary>
/// Options for discovering an IDP's token endpoint and requesting a JWT Authorization Grant.
/// Extends <see cref="RequestJwtAuthGrantOptions"/> semantics but replaces <c>TokenEndpoint</c>
/// with <c>IdpUrl</c>/<c>IdpTokenEndpoint</c> for automatic discovery.
/// </summary>
public sealed class DiscoverAndRequestJwtAuthGrantOptions
{
    /// <summary>
    /// Gets or sets the Identity Provider's base URL for OAuth/OIDC discovery.
    /// Used when <see cref="IdpTokenEndpoint"/> is not specified.
    /// </summary>
    public string? IdpUrl { get; set; }

    /// <summary>
    /// Gets or sets the IDP token endpoint URL. When provided, skips IDP metadata discovery.
    /// </summary>
    public string? IdpTokenEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the MCP authorization server URL (used as the <c>audience</c> parameter).
    /// </summary>
    public required string Audience { get; set; }

    /// <summary>
    /// Gets or sets the MCP resource server URL (used as the <c>resource</c> parameter).
    /// </summary>
    public required string Resource { get; set; }

    /// <summary>
    /// Gets or sets the OIDC ID token to exchange.
    /// </summary>
    public required string IdToken { get; set; }

    /// <summary>
    /// Gets or sets the client ID for authentication with the IDP.
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// Gets or sets the client secret for authentication with the IDP. Optional.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the scopes to request (space-separated). Optional.
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Gets or sets the HTTP client for making requests.
    /// </summary>
    public HttpClient? HttpClient { get; set; }
}
