using System.Text.Json.Serialization;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents the metadata about an OAuth authorization server.
/// </summary>
internal sealed class AuthorizationServerMetadata
{
    /// <summary>
    /// The authorization endpoint URI.
    /// </summary>
    [JsonPropertyName("authorization_endpoint")]
    public Uri AuthorizationEndpoint { get; set; } = null!;

    /// <summary>
    /// The token endpoint URI.
    /// </summary>
    [JsonPropertyName("token_endpoint")]
    public Uri TokenEndpoint { get; set; } = null!;

    /// <summary>
    /// The registration endpoint URI.
    /// </summary>
    [JsonPropertyName("registration_endpoint")]
    public Uri? RegistrationEndpoint { get; set; }

    /// <summary>
    /// The revocation endpoint URI.
    /// </summary>
    [JsonPropertyName("revocation_endpoint")]
    public Uri? RevocationEndpoint { get; set; }

    /// <summary>
    /// The response types supported by the authorization server.
    /// </summary>
    [JsonPropertyName("response_types_supported")]
    public List<string>? ResponseTypesSupported { get; set; }

    /// <summary>
    /// The grant types supported by the authorization server.
    /// </summary>
    [JsonPropertyName("grant_types_supported")]
    public List<string>? GrantTypesSupported { get; set; }

    /// <summary>
    /// The token endpoint authentication methods supported by the authorization server.
    /// </summary>
    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public List<string>? TokenEndpointAuthMethodsSupported { get; set; }

    /// <summary>
    /// The code challenge methods supported by the authorization server.
    /// </summary>
    [JsonPropertyName("code_challenge_methods_supported")]
    public List<string>? CodeChallengeMethodsSupported { get; set; }

    /// <summary>
    /// The issuer URI of the authorization server.
    /// </summary>
    [JsonPropertyName("issuer")]
    public Uri? Issuer { get; set; }

    /// <summary>
    /// The scopes supported by the authorization server.
    /// </summary>
    [JsonPropertyName("scopes_supported")]
    public List<string>? ScopesSupported { get; set; }

    /// <summary>
    /// Indicates if the server supports OAuth Client ID Metadata Documents.
    /// </summary>
    [JsonPropertyName("client_id_metadata_document_supported")]
    public bool ClientIdMetadataDocumentSupported { get; set; }
}
