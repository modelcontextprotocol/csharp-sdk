using System.Text.Json.Serialization;

namespace ModelContextProtocol.TestOAuthServer;

/// <summary>
/// Represents token information for OAuth flow.
/// </summary>
internal sealed class TokenInfo
{
    /// <summary>
    /// Gets or sets the client ID associated with this token.
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// Gets or sets the list of scopes approved for this token.
    /// </summary>
    public List<string> Scopes { get; init; } = [];

    /// <summary>
    /// Gets or sets the issued time of this token.
    /// </summary>
    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>
    /// Gets or sets the expiration time of this token.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Gets or sets the optional resource URI this token is for.
    /// </summary>
    public Uri? Resource { get; init; }

    /// <summary>
    /// Gets or sets the JWT ID for this token.
    /// </summary>
    public string? JwtId { get; init; }
}