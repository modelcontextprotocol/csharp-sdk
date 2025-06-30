using System.Text.Json.Serialization;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents a client registration request for OAuth 2.0 Dynamic Client Registration (RFC 7591).
/// </summary>
internal sealed class DynamicClientRegistrationRequest
{
    /// <summary>
    /// Gets or sets the redirect URIs for the client.
    /// </summary>
    [JsonPropertyName("redirect_uris")]
    public required string[] RedirectUris { get; init; }

    /// <summary>
    /// Gets or sets the token endpoint authentication method.
    /// </summary>
    [JsonPropertyName("token_endpoint_auth_method")]
    public string? TokenEndpointAuthMethod { get; init; }

    /// <summary>
    /// Gets or sets the grant types that the client will use.
    /// </summary>
    [JsonPropertyName("grant_types")]
    public string[]? GrantTypes { get; init; }

    /// <summary>
    /// Gets or sets the response types that the client will use.
    /// </summary>
    [JsonPropertyName("response_types")]
    public string[]? ResponseTypes { get; init; }

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
    /// Gets or sets the scope values that the client will use.
    /// </summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; init; }
}