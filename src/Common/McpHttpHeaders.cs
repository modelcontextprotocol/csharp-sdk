namespace ModelContextProtocol.Protocol;

/// <summary>
/// Constants for MCP-specific HTTP header names used in the Streamable HTTP transport.
/// </summary>
/// <remarks>
/// Per RFC 9110, HTTP header names are case-insensitive. Clients and servers must
/// use case-insensitive comparisons when processing these headers.
/// </remarks>
internal static class McpHttpHeaders
{
    /// <summary>
    /// The draft MCP protocol version string used to gate behaviors that are only enabled
    /// for clients negotiating the in-progress draft specification.
    /// </summary>
    /// <remarks>
    /// Behaviors currently gated on this version include:
    /// <list type="bullet">
    ///   <item><description>
    ///     Requiring the standard MCP request headers (<c>Mcp-Method</c> and <c>Mcp-Name</c>)
    ///     on Streamable HTTP POST requests; servers treat missing headers as errors only when
    ///     the client's <c>MCP-Protocol-Version</c> header matches this value.
    ///   </description></item>
    ///   <item><description>
    ///     Reporting unresolvable resource URIs from <c>resources/read</c> with the standard
    ///     JSON-RPC <see cref="McpErrorCode.InvalidParams"/> (-32602) code rather than the
    ///     legacy <see cref="McpErrorCode.ResourceNotFound"/> (-32002) code.
    ///   </description></item>
    /// </list>
    /// The associated helpers perform exact ordinal matches against this single value rather
    /// than any ordered comparison.
    /// </remarks>
    public static readonly string DraftProtocolVersion = "DRAFT-2026-v1";

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
    /// Key used in <see cref="JsonRpcMessageContext.Items"/> to store the tool
    /// definition for the current request, enabling the transport to add custom parameter headers.
    /// </summary>
    internal const string ToolContextKey = "Mcp.Tool";

    /// <summary>
    /// Protocol versions that require standard MCP request headers (Mcp-Method, Mcp-Name).
    /// </summary>
    private static readonly HashSet<string> s_versionsWithStandardHeaders = new(StringComparer.Ordinal)
    {
        DraftProtocolVersion,
    };

    /// <summary>
    /// Returns <see langword="true"/> if the given protocol version requires standard MCP request headers.
    /// </summary>
    public static bool SupportsStandardHeaders(string? protocolVersion)
        => !string.IsNullOrEmpty(protocolVersion) && s_versionsWithStandardHeaders.Contains(protocolVersion!);

    /// <summary>
    /// Returns <see langword="true"/> if the negotiated protocol version reports unresolvable
    /// resource URIs with the standard JSON-RPC <see cref="McpErrorCode.InvalidParams"/> (-32602)
    /// rather than the legacy <see cref="McpErrorCode.ResourceNotFound"/> (-32002).
    /// </summary>
    internal static bool UseInvalidParamsForMissingResource(string? protocolVersion)
        => string.Equals(protocolVersion, DraftProtocolVersion, StringComparison.Ordinal);
}
