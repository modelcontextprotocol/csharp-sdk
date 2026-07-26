namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents an OAuth error response per RFC 6749 Section 5.2.
/// Used for both token exchange and JWT bearer grant error responses.
/// </summary>
internal sealed class OAuthErrorResponse
{
    /// <summary>
    /// Gets or sets the error code.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets the human-readable error description.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }

    /// <summary>
    /// Gets or sets the URI identifying a human-readable web page with error information.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("error_uri")]
    public string? ErrorUri { get; set; }
}
