namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents a cacheable combination of tokens ready to be used for authentication.
/// </summary>
public sealed class TokenContainer
{
    /// <summary>
    /// Gets or sets the token type (typically "Bearer").
    /// </summary>
    public required string TokenType { get; set; }

    /// <summary>
    /// Gets or sets the access token.
    /// </summary>
    public required string AccessToken { get; set; }

    /// <summary>
    /// Gets or sets the refresh token.
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Gets or sets the number of seconds until the access token expires.
    /// </summary>
    public int? ExpiresIn { get; set; }

    /// <summary>
    /// Gets or sets the scope of the access token.
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the token was obtained.
    /// </summary>
    public required DateTimeOffset ObtainedAt { get; set; }

    internal bool IsExpired => ExpiresIn is not null && DateTimeOffset.UtcNow >= ObtainedAt.AddSeconds(ExpiresIn.Value);
}
