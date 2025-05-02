// filepath: c:\Users\ddelimarsky\source\csharp-sdk-anm\src\ModelContextProtocol\Auth\ClientRegistration.cs
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Auth;

/// <summary>
/// Represents the client registration response.
/// </summary>
public class ClientRegistrationResponse
{
    /// <summary>
    /// OAuth 2.0 client identifier string.
    /// </summary>
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// OAuth 2.0 client secret string.
    /// </summary>
    [JsonPropertyName("client_secret")]
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Time at which the client identifier was issued.
    /// </summary>
    [JsonPropertyName("client_id_issued_at")]
    public long? ClientIdIssuedAt { get; set; }

    /// <summary>
    /// Time at which the client secret will expire or 0 if it will not expire.
    /// </summary>
    [JsonPropertyName("client_secret_expires_at")]
    public long? ClientSecretExpiresAt { get; set; }
}