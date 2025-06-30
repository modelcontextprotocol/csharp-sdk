using System.Text.Json.Serialization;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents a client registration response for OAuth 2.0 Dynamic Client Registration (RFC 7591).
/// </summary>
internal sealed class DynamicClientRegistrationResponse
{
    /// <summary>
    /// Gets or sets the client identifier.
    /// </summary>
    [JsonPropertyName("client_id")]
    public required string ClientId { get; init; }

    /// <summary>
    /// Gets or sets the client secret.
    /// </summary>
    [JsonPropertyName("client_secret")]
    public string? ClientSecret { get; init; }

    /// <summary>
    /// Gets or sets the redirect URIs for the client.
    /// </summary>
    [JsonPropertyName("redirect_uris")]
    public string[]? RedirectUris { get; init; }

    /// <summary>
    /// Gets or sets the token endpoint authentication method.
    /// </summary>
    [JsonPropertyName("token_endpoint_auth_method")]
    public string? TokenEndpointAuthMethod { get; init; }

    /// <summary>
    /// Gets or sets the grant types that the client will use.
    /// </summary>
    [JsonPropertyName("grant_types")]
    public string[]? GrantTypes { get; init; }

    /// <summary>
    /// Gets or sets the response types that the client will use.
    /// </summary>
    [JsonPropertyName("response_types")]
    public string[]? ResponseTypes { get; init; }

    /// <summary>
    /// Gets or sets the client ID issued timestamp.
    /// </summary>
    [JsonPropertyName("client_id_issued_at")]
    public long? ClientIdIssuedAt { get; init; }

    /// <summary>
    /// Gets or sets the client secret expiration time.
    /// </summary>
    [JsonPropertyName("client_secret_expires_at")]
    public long? ClientSecretExpiresAt { get; init; }
}