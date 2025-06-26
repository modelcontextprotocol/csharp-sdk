using System.Text.Json.Serialization;

namespace ModelContextProtocol.TestOAuthServer;

/// <summary>
/// Represents a JSON Web Key Set (JWKS) response.
/// </summary>
internal sealed class JsonWebKeySet
{
    /// <summary>
    /// Gets or sets the array of JSON Web Keys.
    /// </summary>
    [JsonPropertyName("keys")]
    public required JsonWebKey[] Keys { get; init; }
}
