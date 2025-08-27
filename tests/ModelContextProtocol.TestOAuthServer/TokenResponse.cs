using System.Text.Json.Serialization;

namespace ModelContextProtocol.TestOAuthServer;

/// <summary>
/// Represents the token response for OAuth flow.
/// </summary>
internal sealed class TokenResponse
{
    /// <summary>
    /// Gets or sets the access token.
    /// </summary>
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    /// <summary>
    /// Gets or sets the token type.
    /// </summary>
    [JsonPropertyName("token_type")]
    public required string TokenType { get; init; }

    /// <summary>
    /// Gets or sets the token expiration time in seconds.
    /// </summary>
    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; init; }

    /// <summary>
    /// Gets or sets the refresh token.
    /// </summary>
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    /// <summary>
    /// Gets or sets the scope approved for this token.
    /// </summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; init; }
}