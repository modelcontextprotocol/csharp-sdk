namespace ModelContextProtocol.AspNetCore.Authentication;

/// <summary>
/// Provides the validated OAuth transaction and upstream token response used to mint a proxy token.
/// </summary>
public sealed class McpOAuthProxyTokenContext
{
    /// <summary>
    /// Gets the dynamic proxy client identifier.
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// Gets the resource requested by the MCP client, when present.
    /// </summary>
    public string? Resource { get; init; }

    /// <summary>
    /// Gets the scopes granted to the MCP client.
    /// </summary>
    public required IReadOnlyList<string> Scopes { get; init; }

    /// <summary>
    /// Gets the upstream access token. This value must never be returned as the proxy access token.
    /// </summary>
    public required string UpstreamAccessToken { get; init; }

    /// <summary>
    /// Gets the upstream ID token, when present.
    /// </summary>
    public string? UpstreamIdToken { get; init; }

    /// <summary>
    /// Gets the upstream token type, when present.
    /// </summary>
    public string? UpstreamTokenType { get; init; }

    /// <summary>
    /// Gets the upstream token lifetime, when present.
    /// </summary>
    public TimeSpan? UpstreamExpiresIn { get; init; }

    /// <summary>
    /// Gets a value indicating whether the token is being minted during a refresh.
    /// </summary>
    public bool IsRefresh { get; init; }
}

/// <summary>
/// Describes a client-facing access token minted by an OAuth proxy token factory.
/// </summary>
public sealed class McpOAuthProxyTokenResult
{
    /// <summary>
    /// Gets the newly minted access token.
    /// </summary>
    public required string AccessToken { get; init; }

    /// <summary>
    /// Gets the token type returned to the client.
    /// </summary>
    public string TokenType { get; init; } = "Bearer";

    /// <summary>
    /// Gets the access-token lifetime.
    /// </summary>
    public required TimeSpan ExpiresIn { get; init; }

    /// <summary>
    /// Gets the authenticated subject recorded in the token-mint audit event.
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// Gets the token audience recorded in the token-mint audit event.
    /// </summary>
    public required string Audience { get; init; }

    /// <summary>
    /// Gets the unique token identifier recorded in the token-mint audit event.
    /// </summary>
    public required string TokenId { get; init; }
}
