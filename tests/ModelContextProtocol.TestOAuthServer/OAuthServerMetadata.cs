using System.Text.Json.Serialization;

namespace ModelContextProtocol.TestOAuthServer;

/// <summary>
/// Represents the OAuth 2.0 Authorization Server Metadata as defined in RFC 8414.
/// </summary>
internal sealed class OAuthServerMetadata
{
    /// <summary>
    /// Gets or sets the issuer URL.
    /// REQUIRED. The authorization server's issuer identifier, which is a URL that uses the "https" scheme and has no query or fragment components.
    /// </summary>
    [JsonPropertyName("issuer")]
    public required string Issuer { get; init; }

    /// <summary>
    /// Gets or sets the authorization endpoint URL.
    /// URL of the authorization server's authorization endpoint. This is REQUIRED unless no grant types are supported that use the authorization endpoint.
    /// </summary>
    [JsonPropertyName("authorization_endpoint")]
    public required string AuthorizationEndpoint { get; init; }

    /// <summary>
    /// Gets or sets the token endpoint URL.
    /// URL of the authorization server's token endpoint. This is REQUIRED unless only the implicit grant type is supported.
    /// </summary>
    [JsonPropertyName("token_endpoint")]
    public required string TokenEndpoint { get; init; }

    /// <summary>
    /// Gets or sets the JWKS URI.
    /// OPTIONAL. URL of the authorization server's JWK Set document.
    /// </summary>
    [JsonPropertyName("jwks_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JwksUri { get; init; }

    /// <summary>
    /// Gets or sets the registration endpoint URL for dynamic client registration.
    /// OPTIONAL. URL of the authorization server's OAuth 2.0 Dynamic Client Registration endpoint.
    /// </summary>
    [JsonPropertyName("registration_endpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RegistrationEndpoint { get; init; }

    /// <summary>
    /// Gets or sets the scopes supported by this server.
    /// RECOMMENDED. JSON array containing a list of the OAuth 2.0 scope values that this server supports.
    /// </summary>
    [JsonPropertyName("scopes_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ScopesSupported { get; init; }

    /// <summary>
    /// Gets or sets the response types supported by this server.
    /// RECOMMENDED. JSON array containing a list of the OAuth 2.0 "response_type" values that this server supports.
    /// </summary>
    [JsonPropertyName("response_types_supported")]
    public required List<string> ResponseTypesSupported { get; init; }

    /// <summary>
    /// Gets or sets the response modes supported by this server.
    /// OPTIONAL. JSON array containing a list of the OAuth 2.0 "response_mode" values that this server supports.
    /// </summary>
    [JsonPropertyName("response_modes_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ResponseModesSupported { get; init; }

    /// <summary>
    /// Gets or sets the grant types supported by this server.
    /// OPTIONAL. JSON array containing a list of the OAuth 2.0 grant type values that this server supports.
    /// </summary>
    [JsonPropertyName("grant_types_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? GrantTypesSupported { get; init; }

    /// <summary>
    /// Gets or sets the token endpoint authentication methods supported by this server.
    /// OPTIONAL. JSON array containing a list of client authentication methods supported by this token endpoint.
    /// </summary>
    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? TokenEndpointAuthMethodsSupported { get; init; }

    /// <summary>
    /// Gets or sets the token endpoint authentication signing algorithms supported by this server.
    /// OPTIONAL. JSON array containing a list of the JWS signing algorithms supported by the token endpoint.
    /// </summary>
    [JsonPropertyName("token_endpoint_auth_signing_alg_values_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? TokenEndpointAuthSigningAlgValuesSupported { get; init; }

    /// <summary>
    /// Gets or sets the introspection endpoint URL.
    /// OPTIONAL. URL of the authorization server's OAuth 2.0 introspection endpoint.
    /// </summary>
    [JsonPropertyName("introspection_endpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IntrospectionEndpoint { get; init; }

    /// <summary>
    /// Gets or sets the introspection endpoint authentication methods supported by this server.
    /// OPTIONAL. JSON array containing a list of client authentication methods supported by this introspection endpoint.
    /// </summary>
    [JsonPropertyName("introspection_endpoint_auth_methods_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? IntrospectionEndpointAuthMethodsSupported { get; init; }

    /// <summary>
    /// Gets or sets the introspection endpoint authentication signing algorithms supported by this server.
    /// OPTIONAL. JSON array containing a list of the JWS signing algorithms supported by the introspection endpoint.
    /// </summary>
    [JsonPropertyName("introspection_endpoint_auth_signing_alg_values_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? IntrospectionEndpointAuthSigningAlgValuesSupported { get; init; }

    /// <summary>
    /// Gets or sets the revocation endpoint URL.
    /// OPTIONAL. URL of the authorization server's OAuth 2.0 revocation endpoint.
    /// </summary>
    [JsonPropertyName("revocation_endpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RevocationEndpoint { get; init; }

    /// <summary>
    /// Gets or sets the revocation endpoint authentication methods supported by this server.
    /// OPTIONAL. JSON array containing a list of client authentication methods supported by this revocation endpoint.
    /// </summary>
    [JsonPropertyName("revocation_endpoint_auth_methods_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? RevocationEndpointAuthMethodsSupported { get; init; }

    /// <summary>
    /// Gets or sets the revocation endpoint authentication signing algorithms supported by this server.
    /// OPTIONAL. JSON array containing a list of the JWS signing algorithms supported by the revocation endpoint.
    /// </summary>
    [JsonPropertyName("revocation_endpoint_auth_signing_alg_values_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? RevocationEndpointAuthSigningAlgValuesSupported { get; init; }

    /// <summary>
    /// Gets or sets the code challenge methods supported by this server.
    /// OPTIONAL. JSON array containing a list of Proof Key for Code Exchange (PKCE) code challenge methods supported by this server.
    /// </summary>
    [JsonPropertyName("code_challenge_methods_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? CodeChallengeMethodsSupported { get; init; }

    // OpenID Connect specific fields that are commonly included in OAuth metadata
    /// <summary>
    /// Gets or sets the subject types supported by this server.
    /// REQUIRED for OpenID Connect. JSON array containing a list of the Subject Identifier types that this OP supports.
    /// </summary>
    [JsonPropertyName("subject_types_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? SubjectTypesSupported { get; init; }

    /// <summary>
    /// Gets or sets the ID token signing algorithms supported by this server.
    /// REQUIRED for OpenID Connect. JSON array containing a list of the JWS signing algorithms (alg values) supported by the OP for the ID Token.
    /// </summary>
    [JsonPropertyName("id_token_signing_alg_values_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? IdTokenSigningAlgValuesSupported { get; init; }

    /// <summary>
    /// Gets or sets the claims supported by this server.
    /// RECOMMENDED for OpenID Connect. JSON array containing a list of the Claim Names of the Claims that the OpenID Provider MAY be able to supply values for.
    /// </summary>
    [JsonPropertyName("claims_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ClaimsSupported { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether CIMD client IDs are supported.
    /// </summary>
    [JsonPropertyName("client_id_metadata_document_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ClientIdMetadataDocumentSupported { get; init; }
}
