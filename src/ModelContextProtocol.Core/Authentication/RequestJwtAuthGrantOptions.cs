namespace ModelContextProtocol.Authentication;

/// <summary>
/// Options for requesting a JWT Authorization Grant from an Identity Provider via RFC 8693 Token Exchange.
/// </summary>
public sealed class RequestJwtAuthGrantOptions
{
    /// <summary>
    /// Gets or sets the IDP's token endpoint URL.
    /// </summary>
    public required string TokenEndpoint { get; set; }

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
    /// Gets or sets the HTTP client for making requests. If not provided, a default HttpClient will be used.
    /// </summary>
    public HttpClient? HttpClient { get; set; }
}
