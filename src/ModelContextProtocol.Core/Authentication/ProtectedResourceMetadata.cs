using System.Text.Json.Serialization;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents the resource metadata for OAuth authorization as defined in RFC 9396.
/// Defined by <see href="https://datatracker.ietf.org/doc/rfc9728/">RFC 9728</see>.
/// </summary>
public sealed class ProtectedResourceMetadata
{
    /// <summary>
    /// The resource URI.
    /// </summary>
    /// <remarks>
    /// REQUIRED. The protected resource's resource identifier.
    /// </remarks>
    [JsonPropertyName("resource")]
    public required Uri Resource { get; set; }

    /// <summary>
    /// The list of authorization server URIs.
    /// </summary>
    /// <remarks>
    /// OPTIONAL. JSON array containing a list of OAuth authorization server issuer identifiers
    /// for authorization servers that can be used with this protected resource.
    /// </remarks>
    [JsonPropertyName("authorization_servers")]
    public List<Uri> AuthorizationServers { get; set; } = [];

    /// <summary>
    /// The supported bearer token methods.
    /// </summary>
    /// <remarks>
    /// OPTIONAL. JSON array containing a list of the supported methods of sending an OAuth 2.0 bearer token
    /// to the protected resource. Defined values are ["header", "body", "query"].
    /// </remarks>
    [JsonPropertyName("bearer_methods_supported")]
    public List<string> BearerMethodsSupported { get; set; } = ["header"];

    /// <summary>
    /// The supported scopes.
    /// </summary>
    /// <remarks>
    /// RECOMMENDED. JSON array containing a list of scope values that are used in authorization
    /// requests to request access to this protected resource.
    /// </remarks>
    [JsonPropertyName("scopes_supported")]
    public List<string> ScopesSupported { get; set; } = [];

    /// <summary>
    /// URL of the protected resource's JSON Web Key (JWK) Set document.
    /// </summary>
    /// <remarks>
    /// OPTIONAL. This contains public keys belonging to the protected resource, such as signing key(s)
    /// that the resource server uses to sign resource responses. This URL MUST use the https scheme.
    /// </remarks>
    [JsonPropertyName("jwks_uri")]
    public Uri? JwksUri { get; set; }

    /// <summary>
    /// List of the JWS signing algorithms supported by the protected resource for signing resource responses.
    /// </summary>
    /// <remarks>
    /// OPTIONAL. JSON array containing a list of the JWS signing algorithms (alg values) supported by the protected resource
    /// for signing resource responses. No default algorithms are implied if this entry is omitted. The value none MUST NOT be used.
    /// </remarks>
    [JsonPropertyName("resource_signing_alg_values_supported")]
    public List<string>? ResourceSigningAlgValuesSupported { get; set; }

    /// <summary>
    /// Human-readable name of the protected resource intended for display to the end user.
    /// </summary>
    /// <remarks>
    /// RECOMMENDED. It is recommended that protected resource metadata include this field.
    /// The value of this field MAY be internationalized.
    /// </remarks>
    [JsonPropertyName("resource_name")]
    public string? ResourceName { get; set; }

    /// <summary>
    /// The URI to the resource documentation.
    /// </summary>
    /// <remarks>
    /// OPTIONAL. URL of a page containing human-readable information that developers might want or need to know
    /// when using the protected resource.
    /// </remarks>
    [JsonPropertyName("resource_documentation")]
    public Uri? ResourceDocumentation { get; set; }

    /// <summary>
    /// URL of a page containing human-readable information about the protected resource's requirements.
    /// </summary>
    /// <remarks>
    /// OPTIONAL. Information about how the client can use the data provided by the protected resource.
    /// </remarks>
    [JsonPropertyName("resource_policy_uri")]
    public Uri? ResourcePolicyUri { get; set; }

    /// <summary>
    /// URL of a page containing human-readable information about the protected resource's terms of service.
    /// </summary>
    /// <remarks>
    /// OPTIONAL. The value of this field MAY be internationalized.
    /// </remarks>
    [JsonPropertyName("resource_tos_uri")]
    public Uri? ResourceTosUri { get; set; }

    /// <summary>
    /// Boolean value indicating protected resource support for mutual-TLS client certificate-bound access tokens.
    /// </summary>
    /// <remarks>
    /// OPTIONAL. If omitted, the default value is false.
    /// </remarks>
    [JsonPropertyName("tls_client_certificate_bound_access_tokens")]
    public bool? TlsClientCertificateBoundAccessTokens { get; set; }

    /// <summary>
    /// List of the authorization details type values supported by the resource server.
    /// </summary>
    /// <remarks>
    /// OPTIONAL. JSON array containing a list of the authorization details type values supported by the resource server
    /// when the authorization_details request parameter is used.
    /// </remarks>
    [JsonPropertyName("authorization_details_types_supported")]
    public List<string>? AuthorizationDetailsTypesSupported { get; set; }

    /// <summary>
    /// List of the JWS algorithm values supported by the resource server for validating DPoP proof JWTs.
    /// </summary>
    /// <remarks>
    /// OPTIONAL. JSON array containing a list of the JWS alg values supported by the resource server
    /// for validating Demonstrating Proof of Possession (DPoP) proof JWTs.
    /// </remarks>
    [JsonPropertyName("dpop_signing_alg_values_supported")]
    public List<string>? DpopSigningAlgValuesSupported { get; set; }

    /// <summary>
    /// Boolean value specifying whether the protected resource always requires the use of DPoP-bound access tokens.
    /// </summary>
    /// <remarks>
    /// OPTIONAL. If omitted, the default value is false.
    /// </remarks>
    [JsonPropertyName("dpop_bound_access_tokens_required")]
    public bool? DpopBoundAccessTokensRequired { get; set; }
}