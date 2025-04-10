namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// Standard JSON-RPC error codes as defined in the MCP specification.
/// </summary>
internal static class ErrorCodes
{
    /// <summary>
    /// Invalid JSON was received by the server.
    /// </summary>
    public const int ParseError = -32700;

    /// <summary>
    /// The JSON sent is not a valid Request object.
    /// </summary>
    public const int InvalidRequest = -32600;

    /// <summary>
    /// The method does not exist / is not available.
    /// </summary>
    public const int MethodNotFound = -32601;

    /// <summary>
    /// Invalid method parameter(s).
    /// </summary>
    public const int InvalidParams = -32602;

    /// <summary>
    /// Internal JSON-RPC error.
    /// </summary>
    public const int InternalError = -32603;
}