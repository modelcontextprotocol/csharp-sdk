using System.Text.Json.Serialization;

namespace ModelContextProtocol.TestOAuthServer;

/// <summary>
/// Represents the authorization server metadata for OAuth discovery.
/// </summary>
internal sealed class AuthorizationServerMetadata
{
    /// <summary>
    /// Gets or sets the issuer URL.
    /// </summary>
    [JsonPropertyName("issuer")]
    public required Uri Issuer { get; init; }

    /// <summary>
    /// Gets or sets the authorization endpoint URL.
    /// </summary>
    [JsonPropertyName("authorization_endpoint")]
    public required Uri AuthorizationEndpoint { get; init; }

    /// <summary>
    /// Gets or sets the token endpoint URL.
    /// </summary>
    [JsonPropertyName("token_endpoint")]
    public required Uri TokenEndpoint { get; init; }

    /// <summary>
    /// Gets the introspection endpoint URL.
    /// </summary>
    [JsonPropertyName("introspection_endpoint")]
    public Uri? IntrospectionEndpoint => new($"{Issuer}/introspect");

    /// <summary>
    /// Gets or sets the response types supported by this server.
    /// </summary>
    [JsonPropertyName("response_types_supported")]
    public required List<string> ResponseTypesSupported { get; init; }

    /// <summary>
    /// Gets or sets the grant types supported by this server.
    /// </summary>
    [JsonPropertyName("grant_types_supported")]
    public required List<string> GrantTypesSupported { get; init; }

    /// <summary>
    /// Gets or sets the token endpoint authentication methods supported by this server.
    /// </summary>
    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public required List<string> TokenEndpointAuthMethodsSupported { get; init; }

    /// <summary>
    /// Gets or sets the code challenge methods supported by this server.
    /// </summary>
    [JsonPropertyName("code_challenge_methods_supported")]
    public required List<string> CodeChallengeMethodsSupported { get; init; }

    /// <summary>
    /// Gets or sets the scopes supported by this server.
    /// </summary>
    [JsonPropertyName("scopes_supported")]
    public List<string>? ScopesSupported { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether CIMD client IDs are supported.
    /// </summary>
    [JsonPropertyName("client_id_metadata_document_supported")]
    public bool ClientIdMetadataDocumentSupported { get; init; }
}