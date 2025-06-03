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
    /// Indicates that one or more parameters provided in the request are invalid.
    /// </summary>
    /// <remarks>
    /// This error is returned when the parameters do not match the expected method signature or constraints.
    /// This includes cases where required parameters are missing or not understood, such as when a name for
    /// a tool or prompt is not recognized.
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
