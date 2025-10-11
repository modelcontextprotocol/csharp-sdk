using ModelContextProtocol.Protocol;

namespace ModelContextProtocol;

/// <summary>
/// Represents an exception that is thrown when an Model Context Protocol (MCP) error occurs.
/// </summary>
/// <remarks>
/// The <see cref="Exception.Message"/> from a <see cref="McpException"/> may be propagated to the remote
/// endpoint; sensitive information should not be included. If sensitive details need to be included,
/// a different exception type should be used.
///
/// This exception type can be thrown by MCP tools or tool call filters to propogate detailed error messages
/// from <see cref="Exception.Message"/> when a tool execution fails via a <see cref="CallToolResult"/>.
/// For non-tool calls, this exception controls the message propogated via a <see cref="JsonRpcError"/>.
/// 
/// <see cref="McpProtocolException"/> is a derived type that can be used to also specify the
/// <see cref="McpErrorCode"/> that should be used for the resulting <see cref="JsonRpcError"/>.
/// </remarks>
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
    /// Initializes a new instance of the <see cref="McpException"/> class with a specified error message and
    /// a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception, or a null
    /// reference if no inner exception is specified.</param>
    public McpException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}
