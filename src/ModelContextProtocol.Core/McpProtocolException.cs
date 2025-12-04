namespace ModelContextProtocol;

/// <summary>
/// Represents an exception that is thrown when a Model Context Protocol (MCP) protocol-level error occurs.
/// </summary>
/// <remarks>
/// <para>
/// This exception is used to represent failures related to protocol-level concerns, such as malformed
/// JSON-RPC requests, unknown methods, unknown primitive names (tools/prompts/resources), or internal
/// server errors. It is not intended to be used for tool execution errors, including input validation failures.
/// </para>
/// <para>
/// Tool execution errors (including input validation errors, API failures, and business logic errors)
/// should be returned in the result object with <c>IsError</c> set to <see langword="true"/>, allowing
/// language models to see error details and self-correct. Only protocol-level issues should throw
/// <see cref="McpProtocolException"/>.
/// </para>
/// <para>
/// <see cref="Exception.Message"/> or <see cref="ErrorCode"/> from a <see cref="McpProtocolException"/> may be
/// propagated to the remote endpoint; sensitive information should not be included. If sensitive details need
/// to be included, a different exception type should be used.
/// </para>
/// </remarks>
public class McpProtocolException : McpException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpProtocolException"/> class.
    /// </summary>
    public McpProtocolException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpProtocolException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public McpProtocolException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpProtocolException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception, or a null reference if no inner exception is specified.</param>
    public McpProtocolException(string message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpProtocolException"/> class with a specified error message and JSON-RPC error code.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="errorCode">An <see cref="McpErrorCode"/>.</param>
    public McpProtocolException(string message, McpErrorCode errorCode) : this(message, null, errorCode)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpProtocolException"/> class with a specified error message, inner exception, and JSON-RPC error code.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception, or a null reference if no inner exception is specified.</param>
    /// <param name="errorCode">An <see cref="McpErrorCode"/>.</param>
    public McpProtocolException(string message, Exception? innerException, McpErrorCode errorCode) : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Gets the error code associated with this exception.
    /// </summary>
    /// <remarks>
    /// This property contains a standard JSON-RPC error code as defined in the MCP specification. Common error codes include:
    /// <list type="bullet">
    /// <item><description>-32700: Parse error - Invalid JSON received</description></item>
    /// <item><description>-32600: Invalid request - The JSON is not a valid Request object</description></item>
    /// <item><description>-32601: Method not found - The method does not exist or is not available</description></item>
    /// <item><description>-32602: Invalid params - Malformed request or unknown primitive name (tool/prompt/resource)</description></item>
    /// <item><description>-32603: Internal error - Internal JSON-RPC error</description></item>
    /// </list>
    /// </remarks>
    public McpErrorCode ErrorCode { get; } = McpErrorCode.InternalError;
}
