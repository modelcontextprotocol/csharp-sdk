namespace ProtectedMCPClient.Types;

/// <summary>
/// Represents a token response from the OAuth server.
/// </summary>
internal class TokenContainer
{
    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public int ExpiresIn { get; set; }
    public string TokenType { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets the timestamp when the token was obtained.
    /// </summary>
    public DateTimeOffset ObtainedAt { get; set; }
    
    /// <summary>
    /// Gets or sets the timestamp when the token expires.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
