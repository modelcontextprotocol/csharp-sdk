namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Represents errors that occur in MCP transport operations.
/// </summary>
public class McpTransportException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpTransportException"/> class.
    /// </summary>
    public McpTransportException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpTransportException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public McpTransportException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpTransportException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public McpTransportException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
