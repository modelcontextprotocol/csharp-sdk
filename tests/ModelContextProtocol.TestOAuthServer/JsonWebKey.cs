using System.Text.Json.Serialization;

namespace ModelContextProtocol.TestOAuthServer;

/// <summary>
/// Represents a JSON Web Key.
/// </summary>
internal sealed class JsonWebKey
{
    /// <summary>
    /// Gets or sets the key type (e.g., "RSA").
    /// </summary>
    [JsonPropertyName("kty")]
    public required string KeyType { get; init; }

    /// <summary>
    /// Gets or sets the intended use of the key (e.g., "sig" for signature).
    /// </summary>
    [JsonPropertyName("use")]
    public required string Use { get; init; }

    /// <summary>
    /// Gets or sets the key ID.
    /// </summary>
    [JsonPropertyName("kid")]
    public required string KeyId { get; init; }

    /// <summary>
    /// Gets or sets the algorithm intended for use with the key (e.g., "RS256").
    /// </summary>
    [JsonPropertyName("alg")]
    public required string Algorithm { get; init; }

    /// <summary>
    /// Gets or sets the RSA exponent (base64url-encoded).
    /// </summary>
    [JsonPropertyName("e")]
    public required string Exponent { get; init; }

    /// <summary>
    /// Gets or sets the RSA modulus (base64url-encoded).
    /// </summary>
    [JsonPropertyName("n")]
    public required string Modulus { get; init; }
}