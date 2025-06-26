using System.Text.Json.Serialization;

namespace ModelContextProtocol.TestOAuthServer;

/// <summary>
/// Represents authorization code information for OAuth flow.
/// </summary>
internal sealed class AuthorizationCodeInfo
{
    /// <summary>
    /// Gets or sets the client ID associated with this authorization code.
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// Gets or sets the redirect URI associated with this authorization code.
    /// </summary>
    public required string RedirectUri { get; init; }

    /// <summary>
    /// Gets or sets the code challenge associated with this authorization code (for PKCE).
    /// </summary>
    public required string CodeChallenge { get; init; }

    /// <summary>
    /// Gets or sets the list of scopes approved for this authorization code.
    /// </summary>
    public List<string> Scope { get; init; } = [];

    /// <summary>
    /// Gets or sets the optional resource URI this authorization code is for.
    /// </summary>
    public Uri? Resource { get; init; }
}