namespace ModelContextProtocol;

/// <summary>
/// Represents an exception that is thrown by an MCP tool to communicate detailed error information.
/// </summary>
/// <remarks>
/// This exception is used by MCP tools to provide detailed error messages when a tool execution fails.
/// Unlike <see cref="McpException"/>, this exception is intended for application-level errors within tool calls.
/// and does not include JSON-RPC error codes. The <see cref="Exception.Message"/> from this exception
/// will be propagated to the remote endpoint to inform the caller about the tool execution failure.
/// </remarks>
public class McpServerToolException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerToolException"/> class.
    /// </summary>
    public McpServerToolException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerToolException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public McpServerToolException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerToolException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception, or a null reference if no inner exception is specified.</param>
    public McpServerToolException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}
