namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents the response from an RFC 8693 Token Exchange for the JAG flow.
/// Contains the JWT Authorization Grant in the <see cref="AccessToken"/> field.
/// </summary>
internal sealed class JagTokenExchangeResponse
{
    /// <summary>
    /// Gets or sets the issued JAG. Despite the name "access_token" (required by RFC 8693),
    /// this contains a JAG JWT, not an OAuth access token.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = null!;

    /// <summary>
    /// Gets or sets the type of the security token issued.
    /// This MUST be <see cref="IdentityAssertionGrant.TokenTypeIdJag"/>.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("issued_token_type")]
    public string IssuedTokenType { get; set; } = null!;

    /// <summary>
    /// Gets or sets the token type. This MUST be "N_A" per RFC 8693 §2.2.1.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("token_type")]
    public string TokenType { get; set; } = null!;

    /// <summary>
    /// Gets or sets the scope of the issued token, if different from the request.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("scope")]
    public string? Scope { get; set; }

    /// <summary>
    /// Gets or sets the lifetime in seconds of the issued token.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }
}
