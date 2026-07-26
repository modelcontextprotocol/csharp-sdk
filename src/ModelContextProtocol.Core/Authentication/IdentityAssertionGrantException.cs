namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents an error that occurred during a Cross-Application Access authorization operation
/// (token exchange per RFC 8693, and JWT bearer grant per RFC 7523).
/// </summary>
public sealed class IdentityAssertionGrantException : Exception
{
    /// <summary>
    /// Gets the OAuth error code, if available (e.g., "invalid_request", "invalid_grant").
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Gets the human-readable error description from the OAuth error response.
    /// </summary>
    public string? ErrorDescription { get; }

    /// <summary>
    /// Gets the URI identifying a human-readable web page with error information.
    /// </summary>
    public string? ErrorUri { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="IdentityAssertionGrantException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The OAuth error code.</param>
    /// <param name="errorDescription">The human-readable error description.</param>
    /// <param name="errorUri">The error URI.</param>
    public IdentityAssertionGrantException(string message, string? errorCode = null, string? errorDescription = null, string? errorUri = null)
        : base(FormatMessage(message, errorCode, errorDescription))
    {
        ErrorCode = errorCode;
        ErrorDescription = errorDescription;
        ErrorUri = errorUri;
    }

    private static string FormatMessage(string message, string? errorCode, string? errorDescription)
    {
        if (!string.IsNullOrEmpty(errorCode))
        {
            message = $"{message} Error: {errorCode}";
            if (!string.IsNullOrEmpty(errorDescription))
            {
                message = $"{message} ({errorDescription})";
            }
        }
        return message;
    }
}
