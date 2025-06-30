using System.Text.Json.Serialization;

namespace ModelContextProtocol.TestOAuthServer;

/// <summary>
/// Represents the OpenID Connect configuration for discovery.
/// </summary>
internal sealed class OpenIdConnectConfiguration
{
    /// <summary>
    /// Gets or sets the issuer URL.
    /// </summary>
    [JsonPropertyName("issuer")]
    public required string Issuer { get; init; }

    /// <summary>
    /// Gets or sets the authorization endpoint URL.
    /// </summary>
    [JsonPropertyName("authorization_endpoint")]
    public required string AuthorizationEndpoint { get; init; }

    /// <summary>
    /// Gets or sets the token endpoint URL.
    /// </summary>
    [JsonPropertyName("token_endpoint")]
    public required string TokenEndpoint { get; init; }

    /// <summary>
    /// Gets or sets the JWKS URI.
    /// </summary>
    [JsonPropertyName("jwks_uri")]
    public required string JwksUri { get; init; }

    /// <summary>
    /// Gets or sets the response types supported by this server.
    /// </summary>
    [JsonPropertyName("response_types_supported")]
    public required List<string> ResponseTypesSupported { get; init; }

    /// <summary>
    /// Gets or sets the subject types supported by this server.
    /// </summary>
    [JsonPropertyName("subject_types_supported")]
    public required List<string> SubjectTypesSupported { get; init; }

    /// <summary>
    /// Gets or sets the ID token signing algorithms supported by this server.
    /// </summary>
    [JsonPropertyName("id_token_signing_alg_values_supported")]
    public required List<string> IdTokenSigningAlgValuesSupported { get; init; }

    /// <summary>
    /// Gets or sets the scopes supported by this server.
    /// </summary>
    [JsonPropertyName("scopes_supported")]
    public required List<string> ScopesSupported { get; init; }

    /// <summary>
    /// Gets or sets the token endpoint authentication methods supported by this server.
    /// </summary>
    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public required List<string> TokenEndpointAuthMethodsSupported { get; init; }

    /// <summary>
    /// Gets or sets the claims supported by this server.
    /// </summary>
    [JsonPropertyName("claims_supported")]
    public required List<string> ClaimsSupported { get; init; }

    /// <summary>
    /// Gets or sets the code challenge methods supported by this server.
    /// </summary>
    [JsonPropertyName("code_challenge_methods_supported")]
    public required List<string> CodeChallengeMethodsSupported { get; init; }

    /// <summary>
    /// Gets or sets the grant types supported by this server.
    /// </summary>
    [JsonPropertyName("grant_types_supported")]
    public required List<string> GrantTypesSupported { get; init; }

    /// <summary>
    /// Gets or sets the introspection endpoint URL.
    /// </summary>
    [JsonPropertyName("introspection_endpoint")]
    public required string IntrospectionEndpoint { get; init; }

    /// <summary>
    /// Gets or sets the registration endpoint URL for dynamic client registration.
    /// </summary>
    [JsonPropertyName("registration_endpoint")]
    public string? RegistrationEndpoint { get; init; }
}