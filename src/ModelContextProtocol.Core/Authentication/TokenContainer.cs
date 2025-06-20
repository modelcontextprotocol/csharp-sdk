using System.Text.Json.Serialization;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents a token response from the OAuth server.
/// </summary>
internal sealed class TokenContainer
{
    /// <summary>
    /// Gets or sets the access token.
    /// </summary>
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the refresh token.
    /// </summary>
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Gets or sets the number of seconds until the access token expires.
    /// </summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    /// <summary>
    /// Gets or sets the extended expiration time in seconds.
    /// </summary>
    [JsonPropertyName("ext_expires_in")]
    public int ExtExpiresIn { get; set; }

    /// <summary>
    /// Gets or sets the token type (typically "Bearer").
    /// </summary>
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the scope of the access token.
    /// </summary>
    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timestamp when the token was obtained.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset ObtainedAt { get; set; }

    /// <summary>
    /// Gets the timestamp when the token expires, calculated from ObtainedAt and ExpiresIn.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset ExpiresAt => ObtainedAt.AddSeconds(ExpiresIn);
}
