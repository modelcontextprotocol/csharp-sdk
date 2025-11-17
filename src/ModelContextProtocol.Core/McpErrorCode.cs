namespace ModelContextProtocol;

/// <summary>
/// Represents standard JSON-RPC error codes as defined in the MCP specification.
/// </summary>
public enum McpErrorCode
{
    /// <summary>
    /// Indicates that the JSON received could not be parsed.
    /// </summary>
    /// <remarks>
    /// This error occurs when the input contains malformed JSON or incorrect syntax.
    /// </remarks>
    ParseError = -32700,

    /// <summary>
    /// Indicates that the JSON payload does not conform to the expected Request object structure.
    /// </summary>
    /// <remarks>
    /// The request is considered invalid if it lacks required fields or fails to follow the JSON-RPC protocol.
    /// </remarks>
    InvalidRequest = -32600,

    /// <summary>
    /// Indicates that the requested method does not exist or is not available on the server.
    /// </summary>
    /// <remarks>
    /// This error is returned when the method name specified in the request cannot be found.
    /// </remarks>
    MethodNotFound = -32601,

    /// <summary>
    /// Indicates that the request parameters are invalid at the protocol level.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This error is returned for protocol-level parameter issues, such as:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Malformed requests that fail to satisfy the request schema (e.g., CallToolRequest)</description></item>
    /// <item><description>Unknown or unrecognized primitive names (e.g., tool, prompt, or resource names)</description></item>
    /// <item><description>Missing required protocol-level parameters</description></item>
    /// </list>
    /// <para>
    /// Note: Input validation errors within tool/prompt/resource arguments should be reported as execution errors
    /// (e.g., via <see cref="Protocol.CallToolResult.IsError"/>) rather than as protocol errors, allowing language
    /// models to receive error feedback and self-correct.
    /// </para>
    /// </remarks>
    InvalidParams = -32602,

    /// <summary>
    /// Indicates that an internal error occurred while processing the request.
    /// </summary>
    /// <remarks>
    /// This error is used when the endpoint encounters an unexpected condition that prevents it from fulfilling the request.
    /// </remarks>
    InternalError = -32603,
}
