namespace ModelContextProtocol.Client;

/// <summary>
/// Provides details about why an MCP client session completed.
/// </summary>
/// <remarks>
/// <para>
/// Transport implementations may return derived types with additional strongly-typed
/// information, such as <see cref="StdioClientCompletionDetails"/>.
/// </para>
/// </remarks>
public class ClientCompletionDetails
{
    /// <summary>
    /// Gets the exception that caused the session to close, if any.
    /// </summary>
    /// <remarks>
    /// This is <see langword="null"/> for graceful closure.
    /// </remarks>
    public Exception? Exception { get; set; }
}
