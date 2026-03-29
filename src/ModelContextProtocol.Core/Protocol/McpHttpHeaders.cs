namespace ModelContextProtocol.Protocol;

/// <summary>
/// Constants for MCP-specific HTTP header names used in the Streamable HTTP transport.
/// </summary>
/// <remarks>
/// Per RFC 9110, HTTP header names are case-insensitive. Clients and servers must
/// use case-insensitive comparisons when processing these headers.
/// </remarks>
public static class McpHttpHeaders
{
    /// <summary>
    /// The minimum protocol version that requires standard MCP request headers.
    /// </summary>
    /// <remarks>
    /// Servers enforce missing <c>Mcp-Method</c> and <c>Mcp-Name</c> headers as errors only when
    /// the client's <c>MCP-Protocol-Version</c> header indicates this version or later.
    /// Clients using older versions are not required to send these headers.
    /// </remarks>
    public const string MinVersionForStandardHeaders = "DRAFT-2026-v1";

    /// <summary>The session identifier header.</summary>
    public const string SessionId = "Mcp-Session-Id";

    /// <summary>The negotiated protocol version header.</summary>
    public const string ProtocolVersion = "MCP-Protocol-Version";

    /// <summary>The last event ID for SSE stream resumption.</summary>
    public const string LastEventId = "Last-Event-ID";

    /// <summary>
    /// The JSON-RPC method being invoked (e.g., "tools/call", "resources/read").
    /// </summary>
    /// <remarks>
    /// Required on all Streamable HTTP POST requests. The value must match the <c>method</c>
    /// field in the JSON-RPC request body.
    /// </remarks>
    public const string Method = "Mcp-Method";

    /// <summary>
    /// The name or URI of the target resource for the request.
    /// </summary>
    /// <remarks>
    /// Required for <c>tools/call</c>, <c>resources/read</c>, and <c>prompts/get</c> requests.
    /// For <c>tools/call</c> and <c>prompts/get</c>, the value is taken from <c>params.name</c>.
    /// For <c>resources/read</c>, the value is taken from <c>params.uri</c>.
    /// </remarks>
    public const string Name = "Mcp-Name";

    /// <summary>
    /// Prefix for custom parameter headers (<c>Mcp-Param-{Name}</c>).
    /// </summary>
    /// <remarks>
    /// When a tool's <c>inputSchema</c> includes properties annotated with <c>x-mcp-header</c>,
    /// clients mirror those parameter values into HTTP headers using this prefix.
    /// </remarks>
    public const string ParamPrefix = "Mcp-Param-";

    /// <summary>
    /// Key used in <see cref="JsonRpcMessageContext.Items"/> to store the <see cref="Tool"/>
    /// definition for the current request, enabling the transport to add custom parameter headers.
    /// </summary>
    internal const string ToolContextKey = "Mcp.Tool";
}
