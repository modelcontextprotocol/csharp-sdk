using System.Text.Json.Serialization;

namespace ModelContextProtocol.Types.Authentication;

/// <summary>
/// Represents the resource metadata for OAuth authorization.
/// </summary>
public class ProtectedResourceMetadata
{
    /// <summary>
    /// The resource URI.
    /// </summary>
    [JsonPropertyName("resource")]
    public Uri Resource { get; set; } = null!;
    
    /// <summary>
    /// The list of authorization server URIs.
    /// </summary>
    [JsonPropertyName("authorization_servers")]
    public List<Uri> AuthorizationServers { get; set; } = new();
    
    /// <summary>
    /// The supported bearer token methods.
    /// </summary>
    [JsonPropertyName("bearer_methods_supported")]
    public List<string> BearerMethodsSupported { get; set; } = new();
    
    /// <summary>
    /// The supported scopes.
    /// </summary>
    [JsonPropertyName("scopes_supported")]
    public List<string> ScopesSupported { get; set; } = new();
    
    /// <summary>
    /// The URI to the resource documentation.
    /// </summary>
    [JsonPropertyName("resource_documentation")]
    public Uri? ResourceDocumentation { get; set; }
}