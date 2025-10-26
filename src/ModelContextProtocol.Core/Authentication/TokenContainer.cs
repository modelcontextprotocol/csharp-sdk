namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents a cacheable combination of tokens ready to be used for authentication.
/// </summary>
public class TokenContainer
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

    /// <summary>
    /// Gets the timestamp when the token expires, calculated from ObtainedAt and ExpiresIn.
    /// </summary>
    public DateTimeOffset ExpiresAt => ObtainedAt.AddSeconds(ExpiresIn);
}
