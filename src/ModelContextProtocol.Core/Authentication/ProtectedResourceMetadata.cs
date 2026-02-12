using System.Text.Json.Serialization;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents the resource metadata for OAuth authorization as defined in RFC 9396.
/// Defined by <see href="https://datatracker.ietf.org/doc/rfc9728/">RFC 9728</see>.
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
    public List<string> AuthorizationServers { get; set; } = [];

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
    public List<string> ScopesSupported { get; set; } = [];

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
    /// <param name="derivedResourceUri">Optional URI to use for the Resource property if the original Resource is null.</param>
    /// <returns>A new instance of <see cref="ProtectedResourceMetadata"/> with cloned values.</returns>
    public ProtectedResourceMetadata Clone(Uri? derivedResourceUri = null)
    {
        return new ProtectedResourceMetadata
        {
            Resource = Resource ?? derivedResourceUri?.ToString(),
            AuthorizationServers = [.. AuthorizationServers],
            ScopesSupported = [.. ScopesSupported],
        };
    }
}
