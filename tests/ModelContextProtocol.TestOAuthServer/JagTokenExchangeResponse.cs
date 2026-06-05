using System.Text.Json.Serialization;

namespace ModelContextProtocol.TestOAuthServer;

/// <summary>
/// Represents the token exchange response for the Identity Assertion JWT Authorization Grant (ID-JAG)
/// per RFC 8693 / SEP-990.
/// </summary>
internal sealed class JagTokenExchangeResponse
{
    /// <summary>
    /// Gets or sets the issued JWT Authorization Grant (JAG).
    /// Despite the field name "access_token" (required by RFC 8693), this contains a JAG JWT,
    /// not an OAuth access token.
    /// </summary>
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    /// <summary>
    /// Gets or sets the type of security token issued.
    /// For SEP-990, this MUST be "urn:ietf:params:oauth:token-type:id-jag".
    /// </summary>
    [JsonPropertyName("issued_token_type")]
    public required string IssuedTokenType { get; init; }

    /// <summary>
    /// Gets or sets the token type.
    /// For SEP-990, this MUST be "N_A" per RFC 8693 §2.2.1 because the JAG is not an access token.
    /// </summary>
    [JsonPropertyName("token_type")]
    public required string TokenType { get; init; }

    /// <summary>
    /// Gets or sets the lifetime in seconds of the issued JAG.
    /// </summary>
    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; init; }
}
