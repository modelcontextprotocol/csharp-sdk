using System.Text.Json.Serialization;

namespace ModelContextProtocol.TestOAuthServer;

/// <summary>
/// Represents a client registration request as defined in RFC 7591.
/// </summary>
internal sealed class ClientRegistrationRequest
{
    /// <summary>
    /// Gets or sets the redirect URIs for the client.
    /// </summary>
    [JsonPropertyName("redirect_uris")]
    public required List<string> RedirectUris { get; init; }

    /// <summary>
    /// Gets or sets the token endpoint authentication method.
    /// </summary>
    [JsonPropertyName("token_endpoint_auth_method")]
    public string? TokenEndpointAuthMethod { get; init; }

    /// <summary>
    /// Gets or sets the grant types that the client will use.
    /// </summary>
    [JsonPropertyName("grant_types")]
    public List<string>? GrantTypes { get; init; }

    /// <summary>
    /// Gets or sets the response types that the client will use.
    /// </summary>
    [JsonPropertyName("response_types")]
    public List<string>? ResponseTypes { get; init; }

    /// <summary>
    /// Gets or sets the human-readable name of the client.
    /// </summary>
    [JsonPropertyName("client_name")]
    public string? ClientName { get; init; }

    /// <summary>
    /// Gets or sets the URL of the client's home page.
    /// </summary>
    [JsonPropertyName("client_uri")]
    public string? ClientUri { get; init; }

    /// <summary>
    /// Gets or sets the URL for the client's logo.
    /// </summary>
    [JsonPropertyName("logo_uri")]
    public string? LogoUri { get; init; }

    /// <summary>
    /// Gets or sets the scope values that the client will use.
    /// </summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    /// <summary>
    /// Gets or sets the contacts for the client.
    /// </summary>
    [JsonPropertyName("contacts")]
    public List<string>? Contacts { get; init; }

    /// <summary>
    /// Gets or sets the URL for the client's terms of service.
    /// </summary>
    [JsonPropertyName("tos_uri")]
    public string? TosUri { get; init; }

    /// <summary>
    /// Gets or sets the URL for the client's privacy policy.
    /// </summary>
    [JsonPropertyName("policy_uri")]
    public string? PolicyUri { get; init; }

    /// <summary>
    /// Gets or sets the JWK Set URL for the client.
    /// </summary>
    [JsonPropertyName("jwks_uri")]
    public string? JwksUri { get; init; }

    /// <summary>
    /// Gets or sets the software identifier for the client.
    /// </summary>
    [JsonPropertyName("software_id")]
    public string? SoftwareId { get; init; }

    /// <summary>
    /// Gets or sets the software version for the client.
    /// </summary>
    [JsonPropertyName("software_version")]
    public string? SoftwareVersion { get; init; }
}