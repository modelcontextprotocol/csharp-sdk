using System.Text.Json.Serialization;

namespace ModelContextProtocol.TestOAuthServer;

/// <summary>
/// Represents the response from the token introspection endpoint.
/// </summary>
internal sealed class TokenIntrospectionResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether the token is active.
    /// </summary>
    [JsonPropertyName("active")]
    public required bool Active { get; init; }

    /// <summary>
    /// Gets or sets the client ID associated with the token.
    /// </summary>
    [JsonPropertyName("client_id")]
    public string? ClientId { get; init; }

    /// <summary>
    /// Gets or sets the scope of the token.
    /// </summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    /// <summary>
    /// Gets or sets the expiration timestamp of the token (Unix timestamp).
    /// </summary>
    [JsonPropertyName("exp")]
    public long? ExpirationTime { get; init; }

    /// <summary>
    /// Gets or sets the audience of the token.
    /// </summary>
    [JsonPropertyName("aud")]
    public string? Audience { get; init; }
}