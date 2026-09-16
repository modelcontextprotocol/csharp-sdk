namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents the response from a JWT Bearer grant (RFC 7523) access token request.
/// </summary>
internal sealed class JwtBearerAccessTokenResponse
{
    /// <summary>
    /// Gets or sets the OAuth access token.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = null!;

    /// <summary>
    /// Gets or sets the token type. This should be "Bearer".
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("token_type")]
    public string TokenType { get; set; } = null!;

    /// <summary>
    /// Gets or sets the lifetime in seconds of the access token.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }

    /// <summary>
    /// Gets or sets the refresh token.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Gets or sets the scope of the access token.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("scope")]
    public string? Scope { get; set; }
}
