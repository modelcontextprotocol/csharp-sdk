using System.Text.Json.Serialization;

namespace ModelContextProtocol.AspNetCore.Authentication;

internal sealed class OAuthProxyClientRegistrationRequest
{
    [JsonPropertyName("redirect_uris")]
    public string[]? RedirectUris { get; set; }

    [JsonPropertyName("token_endpoint_auth_method")]
    public string? TokenEndpointAuthMethod { get; set; }

    [JsonPropertyName("grant_types")]
    public string[]? GrantTypes { get; set; }

    [JsonPropertyName("response_types")]
    public string[]? ResponseTypes { get; set; }

    [JsonPropertyName("client_name")]
    public string? ClientName { get; set; }

    [JsonPropertyName("client_uri")]
    public string? ClientUri { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("application_type")]
    public string? ApplicationType { get; set; }
}

internal sealed class OAuthProxyClientRegistrationResponse
{
    [JsonPropertyName("client_id")]
    public required string ClientId { get; init; }

    [JsonPropertyName("client_id_issued_at")]
    public required long ClientIdIssuedAt { get; init; }

    [JsonPropertyName("redirect_uris")]
    public required string[] RedirectUris { get; init; }

    [JsonPropertyName("token_endpoint_auth_method")]
    public string TokenEndpointAuthMethod { get; init; } = "none";

    [JsonPropertyName("grant_types")]
    public string[] GrantTypes { get; init; } = ["authorization_code", "refresh_token"];

    [JsonPropertyName("response_types")]
    public string[] ResponseTypes { get; init; } = ["code"];

    [JsonPropertyName("client_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientName { get; init; }

    [JsonPropertyName("client_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientUri { get; init; }

    [JsonPropertyName("scope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scope { get; init; }

    [JsonPropertyName("application_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ApplicationType { get; init; }
}

internal sealed class OAuthProxyAuthorizationServerMetadata
{
    [JsonPropertyName("issuer")]
    public required string Issuer { get; init; }

    [JsonPropertyName("authorization_endpoint")]
    public required string AuthorizationEndpoint { get; init; }

    [JsonPropertyName("token_endpoint")]
    public required string TokenEndpoint { get; init; }

    [JsonPropertyName("registration_endpoint")]
    public required string RegistrationEndpoint { get; init; }

    [JsonPropertyName("jwks_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JwksUri { get; init; }

    [JsonPropertyName("scopes_supported")]
    public required string[] ScopesSupported { get; init; }

    [JsonPropertyName("response_types_supported")]
    public string[] ResponseTypesSupported { get; init; } = ["code"];

    [JsonPropertyName("grant_types_supported")]
    public string[] GrantTypesSupported { get; init; } = ["authorization_code", "refresh_token"];

    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public string[] TokenEndpointAuthMethodsSupported { get; init; } = ["none"];

    [JsonPropertyName("code_challenge_methods_supported")]
    public string[] CodeChallengeMethodsSupported { get; init; } = ["S256"];

    [JsonPropertyName("authorization_response_iss_parameter_supported")]
    public bool AuthorizationResponseIssuerParameterSupported { get; init; } = true;
}

internal sealed class OAuthProxyErrorResponse
{
    [JsonPropertyName("error")]
    public required string Error { get; init; }

    [JsonPropertyName("error_description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorDescription { get; init; }
}

internal sealed class OAuthProxyTokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("token_type")]
    public required string TokenType { get; init; }

    [JsonPropertyName("expires_in")]
    public required long ExpiresIn { get; init; }

    [JsonPropertyName("refresh_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("scope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scope { get; init; }
}

internal sealed class OAuthProxyUpstreamMetadata
{
    [JsonPropertyName("issuer")]
    public string? Issuer { get; set; }

    [JsonPropertyName("authorization_endpoint")]
    public string? AuthorizationEndpoint { get; set; }

    [JsonPropertyName("token_endpoint")]
    public string? TokenEndpoint { get; set; }

    [JsonPropertyName("code_challenge_methods_supported")]
    public string[]? CodeChallengeMethodsSupported { get; set; }
}

internal sealed class OAuthProxyUpstreamTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("id_token")]
    public string? IdToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public long? ExpiresIn { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

internal sealed class OAuthProxyClientRecord
{
    public required string ClientId { get; set; }
    public required string[] RedirectUris { get; set; }
    public required string[] Scopes { get; set; }
    public string? ClientName { get; set; }
    public string? ClientUri { get; set; }
    public string? ApplicationType { get; set; }
    public bool SupportsRefreshTokens { get; set; }
}

internal sealed class OAuthProxyAuthorizationTransaction
{
    public required string ClientId { get; set; }
    public required string RedirectUri { get; set; }
    public required string CodeChallenge { get; set; }
    public required string UpstreamCodeVerifier { get; set; }
    public required string BrowserBinding { get; set; }
    public required string[] Scopes { get; set; }
    public string? ClientState { get; set; }
    public string? Resource { get; set; }
    public bool SupportsRefreshTokens { get; set; }
}

internal sealed class OAuthProxyAuthorizationCode
{
    public required string ClientId { get; set; }
    public required string RedirectUri { get; set; }
    public required string CodeChallenge { get; set; }
    public required string[] Scopes { get; set; }
    public string? Resource { get; set; }
    public bool SupportsRefreshTokens { get; set; }
    public required OAuthProxyUpstreamTokenResponse UpstreamToken { get; set; }
}

internal sealed class OAuthProxyRefreshRecord
{
    public required string ClientId { get; set; }
    public required string[] Scopes { get; set; }
    public string? Resource { get; set; }
    public required OAuthProxyUpstreamTokenResponse UpstreamToken { get; set; }
    public required string FamilyId { get; set; }
    public required DateTimeOffset ExpiresAt { get; set; }
}

internal sealed class OAuthProxyRefreshFamilyRecord
{
    public required string CurrentRefreshToken { get; set; }
    public required DateTimeOffset ExpiresAt { get; set; }
}

internal sealed class OAuthProxyConsumedRefreshRecord
{
    public required string FamilyId { get; set; }
    public required DateTimeOffset ExpiresAt { get; set; }
}

[JsonSerializable(typeof(OAuthProxyClientRegistrationRequest))]
[JsonSerializable(typeof(OAuthProxyClientRegistrationResponse))]
[JsonSerializable(typeof(OAuthProxyAuthorizationServerMetadata))]
[JsonSerializable(typeof(OAuthProxyErrorResponse))]
[JsonSerializable(typeof(OAuthProxyTokenResponse))]
[JsonSerializable(typeof(OAuthProxyUpstreamMetadata))]
[JsonSerializable(typeof(OAuthProxyUpstreamTokenResponse))]
[JsonSerializable(typeof(OAuthProxyClientRecord))]
[JsonSerializable(typeof(OAuthProxyAuthorizationTransaction))]
[JsonSerializable(typeof(OAuthProxyAuthorizationCode))]
[JsonSerializable(typeof(OAuthProxyRefreshRecord))]
[JsonSerializable(typeof(OAuthProxyRefreshFamilyRecord))]
[JsonSerializable(typeof(OAuthProxyConsumedRefreshRecord))]
internal sealed partial class McpOAuthProxyJsonContext : JsonSerializerContext;
