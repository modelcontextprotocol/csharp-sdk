namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents a cacheable token representation.
/// </summary>
public class TokenContainerCacheable
{
    /// <summary>
    /// Gets or sets the access token.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the refresh token.
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Gets or sets the number of seconds until the access token expires.
    /// </summary>
    public int ExpiresIn { get; set; }

    /// <summary>
    /// Gets or sets the extended expiration time in seconds.
    /// </summary>
    public int ExtExpiresIn { get; set; }

    /// <summary>
    /// Gets or sets the token type (typically "Bearer").
    /// </summary>
    public string TokenType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the scope of the access token.
    /// </summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timestamp when the token was obtained.
    /// </summary>
    public DateTimeOffset ObtainedAt { get; set; }
}
