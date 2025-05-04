namespace ProtectedMCPClient;

/// <summary>
/// Represents a token response from the OAuth server.
/// </summary>
internal class TokenContainer
{
    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public int ExpiresIn { get; set; }
    public string TokenType { get; set; } = string.Empty;
}
