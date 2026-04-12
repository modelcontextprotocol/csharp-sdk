namespace ModelContextProtocol.Authentication;

/// <summary>
/// Options for exchanging a JWT Authorization Grant for an access token via RFC 7523.
/// </summary>
public sealed class ExchangeJwtBearerGrantOptions
{
    /// <summary>
    /// Gets or sets the MCP Server's authorization server token endpoint URL.
    /// </summary>
    public required string TokenEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the JWT Authorization Grant (JAG) assertion obtained from token exchange.
    /// </summary>
    public required string Assertion { get; set; }

    /// <summary>
    /// Gets or sets the client ID for authentication with the MCP authorization server.
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// Gets or sets the client secret for authentication with the MCP authorization server. Optional.
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
