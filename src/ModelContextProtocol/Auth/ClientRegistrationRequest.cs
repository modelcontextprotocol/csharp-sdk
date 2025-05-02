using System.Text.Json.Serialization;

namespace ModelContextProtocol.Auth;

/// <summary>
/// Represents the client registration request metadata.
/// </summary>
public class ClientRegistrationRequest
{
    /// <summary>
    /// Array of redirection URI strings for use in redirect-based flows.
    /// </summary>
    [JsonPropertyName("redirect_uris")]
    public List<string> RedirectUris { get; set; } = new();

    /// <summary>
    /// String indicator of the requested authentication method for the token endpoint.
    /// </summary>
    [JsonPropertyName("token_endpoint_auth_method")]
    public string? TokenEndpointAuthMethod { get; set; }

    /// <summary>
    /// Array of OAuth 2.0 grant type strings that the client can use at the token endpoint.
    /// </summary>
    [JsonPropertyName("grant_types")]
    public List<string>? GrantTypes { get; set; }

    /// <summary>
    /// Array of the OAuth 2.0 response type strings that the client can use at the authorization endpoint.
    /// </summary>
    [JsonPropertyName("response_types")]
    public List<string>? ResponseTypes { get; set; }

    /// <summary>
    /// Human-readable string name of the client to be presented to the end-user during authorization.
    /// </summary>
    [JsonPropertyName("client_name")]
    public string? ClientName { get; set; }

    /// <summary>
    /// URL string of a web page providing information about the client.
    /// </summary>
    [JsonPropertyName("client_uri")]
    public string? ClientUri { get; set; }

    /// <summary>
    /// URL string that references a logo for the client.
    /// </summary>
    [JsonPropertyName("logo_uri")]
    public string? LogoUri { get; set; }

    /// <summary>
    /// String containing a space-separated list of scope values that the client can use.
    /// </summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    /// <summary>
    /// Array of strings representing ways to contact people responsible for this client.
    /// </summary>
    [JsonPropertyName("contacts")]
    public List<string>? Contacts { get; set; }

    /// <summary>
    /// URL string that points to a human-readable terms of service document for the client.
    /// </summary>
    [JsonPropertyName("tos_uri")]
    public string? TosUri { get; set; }

    /// <summary>
    /// URL string that points to a human-readable privacy policy document.
    /// </summary>
    [JsonPropertyName("policy_uri")]
    public string? PolicyUri { get; set; }

    /// <summary>
    /// URL string referencing the client's JSON Web Key (JWK) Set document.
    /// </summary>
    [JsonPropertyName("jwks_uri")]
    public string? JwksUri { get; set; }

    /// <summary>
    /// Client's JSON Web Key Set document value.
    /// </summary>
    [JsonPropertyName("jwks")]
    public object? Jwks { get; set; }

    /// <summary>
    /// A unique identifier string assigned by the client developer or software publisher.
    /// </summary>
    [JsonPropertyName("software_id")]
    public string? SoftwareId { get; set; }

    /// <summary>
    /// A version identifier string for the client software identified by software_id.
    /// </summary>
    [JsonPropertyName("software_version")]
    public string? SoftwareVersion { get; set; }
}
