namespace ModelContextProtocol;

/// <summary>
/// Represents an exception that is thrown when a Model Context Protocol (MCP) error occurs.
/// </summary>
public class McpException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpException"/> class.
    /// </summary>
    public McpException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public McpException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception, or a null reference if no inner exception is specified.</param>
    public McpException(string message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpException"/> class with a specified error message and JSON-RPC error code.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="errorCode">A JSON-RPC error code from <see cref="Protocol.Messages.ErrorCodes"/> class.</param>
    public McpException(string message, int? errorCode) : this(message, null, errorCode)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpException"/> class with a specified error message, inner exception, and JSON-RPC error code.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception, or a null reference if no inner exception is specified.</param>
    /// <param name="errorCode">A JSON-RPC error code from <see cref="Protocol.Messages.ErrorCodes"/> class.</param>
    public McpException(string message, Exception? innerException, int? errorCode) : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Gets the JSON-RPC error code associated with this exception.
    /// </summary>
    /// <value>
    /// A standard JSON-RPC error code, or <see langword="null"/> if the exception wasn't caused by a JSON-RPC error.
    /// </value>
    /// <remarks>
    /// This property contains a standard JSON-RPC error code as defined in the MCP specification. Common error codes include:
    /// <list type="bullet">
    /// <item><description>-32700: Parse error - Invalid JSON received</description></item>
    /// <item><description>-32600: Invalid request - The JSON is not a valid Request object</description></item>
    /// <item><description>-32601: Method not found - The method does not exist or is not available</description></item>
    /// <item><description>-32602: Invalid params - Invalid method parameters</description></item>
    /// <item><description>-32603: Internal error - Internal JSON-RPC error</description></item>
    /// </list>
    /// </remarks>
    public int? ErrorCode { get; }
}