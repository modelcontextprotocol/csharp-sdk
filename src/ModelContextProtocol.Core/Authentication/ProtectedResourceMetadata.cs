using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents the resource metadata for OAuth authorization as defined in <see href="https://datatracker.ietf.org/doc/rfc9728/">RFC 9728</see>.
/// </summary>
public sealed class ProtectedResourceMetadata
{
    /// <summary>
    /// Gets or sets the resource URI.
    /// </summary>
    /// <value>
    /// The protected resource's resource identifier.
    /// </value>
    /// <remarks>
    /// OPTIONAL. When omitted, the MCP authentication handler infers the resource URI from the incoming request only when serving
    /// the default <c>/.well-known/oauth-protected-resource</c> endpoint. If a custom <c>ResourceMetadataUri</c> is configured,
    /// <b>Resource</b> must be explicitly set. Automatic inference only works with the default endpoint pattern.
    /// </remarks>
    [JsonPropertyName("resource")]
    [StringSyntax(StringSyntaxAttribute.Uri)]
    public string? Resource { get; set; }

    /// <summary>
    /// Gets or sets the list of authorization server URIs.
    /// </summary>
    /// <value>
    /// A JSON array containing a list of OAuth authorization server issuer identifiers
    /// for authorization servers that can be used with this protected resource.
    /// </value>
    /// <remarks>
    /// OPTIONAL.
    /// </remarks>
    [JsonPropertyName("authorization_servers")]
    public IList<string> AuthorizationServers { get; set; } = [];

    /// <summary>
    /// Gets or sets the supported bearer token methods.
    /// </summary>
    /// <value>
    /// A JSON array containing a list of the supported methods of sending an OAuth 2.0 bearer token
    /// to the protected resource. Defined values are ["header", "body", "query"].
    /// </value>
    /// <remarks>
    /// OPTIONAL.
    /// </remarks>
    [JsonPropertyName("bearer_methods_supported")]
    public IList<string> BearerMethodsSupported { get; set; } = ["header"];

    /// <summary>
    /// Gets or sets the supported scopes.
    /// </summary>
    /// <value>
    /// A JSON array containing a list of scope values that are used in authorization
    /// requests to request access to this protected resource.
    /// </value>
    /// <remarks>
    /// RECOMMENDED.
    /// </remarks>
    [JsonPropertyName("scopes_supported")]
    public IList<string> ScopesSupported { get; set; } = [];

    /// <summary>
    /// Gets or sets the URL of the protected resource's JSON Web Key (JWK) Set document.
    /// </summary>
    /// <remarks>
    /// OPTIONAL. This document contains public keys belonging to the protected resource, such as signing keys
    /// that the resource server uses to sign resource responses. This URL MUST use the HTTPS scheme.
    /// </remarks>
    [JsonPropertyName("jwks_uri")]
    [StringSyntax(StringSyntaxAttribute.Uri)]
    public string? JwksUri { get; set; }

    /// <summary>
    /// Gets or sets the list of the JWS signing algorithms supported by the protected resource for signing resource responses.
    /// </summary>
    /// <value>
    /// A JSON array containing a list of the JWS signing algorithms (alg values) supported by the protected resource
    /// for signing resource responses.
    /// </value>
    /// <remarks>
    /// OPTIONAL. No default algorithms are implied if this entry is omitted. The value "none" MUST NOT be used.
    /// </remarks>
    [JsonPropertyName("resource_signing_alg_values_supported")]
    public IList<string>? ResourceSigningAlgValuesSupported { get; set; }

    /// <summary>
    /// Gets or sets the human-readable name of the protected resource intended for display to the end user.
    /// </summary>
    /// <remarks>
    /// RECOMMENDED. It is recommended that protected resource metadata include this field.
    /// The value of this field MAY be internationalized.
    /// </remarks>
    [JsonPropertyName("resource_name")]
    public string? ResourceName { get; set; }

    /// <summary>
    /// Gets or sets the URI to the resource documentation.
    /// </summary>
    /// <value>
    /// The URL of a page containing human-readable information that developers might want or need to know
    /// when using the protected resource.
    /// </value>
    /// <remarks>
    /// OPTIONAL.
    /// </remarks>
    [JsonPropertyName("resource_documentation")]
    [StringSyntax(StringSyntaxAttribute.Uri)]
    public string? ResourceDocumentation { get; set; }

    /// <summary>
    /// Gets or sets the URL of a page containing human-readable information about the protected resource's requirements.
    /// </summary>
    /// <value>
    /// The URL of a page that contains information about how the client can use the data provided by the protected resource.
    /// </value>
    /// <remarks>
    /// OPTIONAL.
    /// </remarks>
    [JsonPropertyName("resource_policy_uri")]
    [StringSyntax(StringSyntaxAttribute.Uri)]
    public string? ResourcePolicyUri { get; set; }

    /// <summary>
    /// Gets or sets the URL of a page containing human-readable information about the protected resource's terms of service.
    /// </summary>
    /// <remarks>
    /// OPTIONAL. The value of this field MAY be internationalized.
    /// </remarks>
    [JsonPropertyName("resource_tos_uri")]
    [StringSyntax(StringSyntaxAttribute.Uri)]
    public string? ResourceTosUri { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether there is protected resource support for mutual-TLS client certificate-bound access tokens.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if there's protected resource support for mutual-TLS client certificate-bound access tokens; otherwise, <see langword="false"/>. The default is <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// OPTIONAL.
    /// </remarks>
    [JsonPropertyName("tls_client_certificate_bound_access_tokens")]
    public bool? TlsClientCertificateBoundAccessTokens { get; set; }

    /// <summary>
    /// Gets or sets the list of the authorization details type values supported by the resource server.
    /// </summary>
    /// <value>
    /// A JSON array containing a list of the authorization details type values supported by the resource server
    /// when the authorization_details request parameter is used.
    /// </value>
    /// <remarks>
    /// OPTIONAL.
    /// </remarks>
    [JsonPropertyName("authorization_details_types_supported")]
    public IList<string>? AuthorizationDetailsTypesSupported { get; set; }

    /// <summary>
    /// Gets or sets the list of the JWS algorithm values supported by the resource server for validating DPoP proof JWTs.
    /// </summary>
    /// <value>
    /// A JSON array containing a list of the JWS alg values supported by the resource server
    /// for validating Demonstrating Proof of Possession (DPoP) proof JWTs.
    /// </value>
    /// <remarks>
    /// OPTIONAL.
    /// </remarks>
    [JsonPropertyName("dpop_signing_alg_values_supported")]
    public IList<string>? DpopSigningAlgValuesSupported { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the protected resource always requires the use of DPoP-bound access tokens.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if the protected resource always requires the use of DPoP-bound access tokens; otherwise, <see langword="false"/>. The default is <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// OPTIONAL.
    /// </remarks>
    [JsonPropertyName("dpop_bound_access_tokens_required")]
    public bool? DpopBoundAccessTokensRequired { get; set; }

    /// <summary>
    /// Used internally by the client to get or set the scope specified as a WWW-Authenticate header parameter.
    /// This should be preferred over using the ScopesSupported property.
    ///
    /// The scopes included in the WWW-Authenticate challenge MAY match scopes_supported, be a subset or superset of it,
    /// or an alternative collection that is neither a strict subset nor superset. Clients MUST NOT assume any particular
    /// set relationship between the challenged scope set and scopes_supported. Clients MUST treat the scopes provided
    /// in the challenge as authoritative for satisfying the current request.
    ///
    /// https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization#protected-resource-metadata-discovery-requirements
    /// </summary>
    [JsonIgnore]
    internal string? WwwAuthenticateScope { get; set; }

    /// <summary>
    /// Creates a deep copy of this <see cref="ProtectedResourceMetadata"/> instance, optionally overriding the Resource property.
    /// </summary>
    /// <param name="derivedResource">Optional resource URI string to use for the Resource property if the original Resource is null.</param>
    /// <returns>A new instance of <see cref="ProtectedResourceMetadata"/> with cloned values.</returns>
    public ProtectedResourceMetadata Clone(string? derivedResource = null)
    {
        return new ProtectedResourceMetadata
        {
            Resource = Resource ?? derivedResource,
            AuthorizationServers = [.. AuthorizationServers],
            BearerMethodsSupported = [.. BearerMethodsSupported],
            ScopesSupported = [.. ScopesSupported],
            JwksUri = JwksUri,
            ResourceSigningAlgValuesSupported = ResourceSigningAlgValuesSupported is not null ? [.. ResourceSigningAlgValuesSupported] : null,
            ResourceName = ResourceName,
            ResourceDocumentation = ResourceDocumentation,
            ResourcePolicyUri = ResourcePolicyUri,
            ResourceTosUri = ResourceTosUri,
            TlsClientCertificateBoundAccessTokens = TlsClientCertificateBoundAccessTokens,
            AuthorizationDetailsTypesSupported = AuthorizationDetailsTypesSupported is not null ? [.. AuthorizationDetailsTypesSupported] : null,
            DpopSigningAlgValuesSupported = DpopSigningAlgValuesSupported is not null ? [.. DpopSigningAlgValuesSupported] : null,
            DpopBoundAccessTokensRequired = DpopBoundAccessTokensRequired
        };
    }
}
