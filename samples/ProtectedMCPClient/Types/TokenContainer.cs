using System.Text.Json.Serialization;

namespace ProtectedMCPClient.Types;

/// <summary>
/// Represents a token response from the OAuth server.
/// </summary>
internal class TokenContainer
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;
    
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }
    
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
    
    [JsonPropertyName("ext_expires_in")]
    public int ExtExpiresIn { get; set; }
    
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;
    
    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets the timestamp when the token was obtained.
    /// </summary>
    public DateTimeOffset ObtainedAt { get; set; }
    
    /// <summary>
    /// Gets the timestamp when the token expires, calculated from ObtainedAt and ExpiresIn.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset ExpiresAt => ObtainedAt.AddSeconds(ExpiresIn);
}
