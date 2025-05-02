using System.Text.Json.Serialization;

namespace ModelContextProtocol.Auth;

/// <summary>
/// Represents an OAuth token response.
/// </summary>
public class OAuthToken
{
    /// <summary>
    /// The access token issued by the authorization server.
    /// </summary>
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// The type of token issued.
    /// </summary>
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    /// <summary>
    /// The lifetime in seconds of the access token.
    /// </summary>
    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }

    /// <summary>
    /// The refresh token used to obtain new access tokens using the same authorization grant.
    /// </summary>
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    /// <summary>
    /// The scopes associated with the access token.
    /// </summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    /// <summary>
    /// An ID token as a JWT (JSON Web Token).
    /// </summary>
    [JsonPropertyName("id_token")]
    public string? IdToken { get; set; }
}