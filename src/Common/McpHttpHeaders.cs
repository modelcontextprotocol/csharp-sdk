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
    /// The 2026-07-28 MCP protocol revision (SEP-2575 + SEP-2567). It removed the <c>initialize</c>
    /// handshake and <c>Mcp-Session-Id</c>, so Streamable HTTP no longer has sessions; it also enabled
    /// MRTR (SEP-2322) and made the standard MCP request headers (<c>Mcp-Method</c>, <c>Mcp-Name</c>)
    /// required. Behaviors that began at this revision are gated by ordinal-comparing the per-request
    /// version against it (see <see cref="IsJuly2026OrLaterProtocolVersion"/>), so it underpins the more
    /// semantically named helpers. It is also the latest revision this SDK supports, so clients prefer it
    /// by default.
    /// </summary>
    public const string July2026ProtocolVersion = "2026-07-28";

    /// <summary>
    /// The 2025-11-25 MCP protocol revision: the latest revision that still supports Streamable HTTP
    /// sessions (the <c>initialize</c> handshake and <c>Mcp-Session-Id</c>); newer revisions remove them.
    /// It is the default version for the <c>initialize</c> and session-resume code paths, and the version
    /// the server advertises when a peer requests an unsupported version on the initialize handshake.
    /// </summary>
    public const string November2025ProtocolVersion = "2025-11-25";

    /// <summary>
    /// Protocol versions that still use the <c>initialize</c> handshake.
    /// </summary>
    internal static readonly string[] InitializeHandshakeProtocolVersions =
    [
        "2024-11-05",
        "2025-03-26",
        "2025-06-18",
        November2025ProtocolVersion,
    ];

    /// <summary>
    /// Protocol versions that use per-request metadata instead of the <c>initialize</c> handshake.
    /// </summary>
    internal static readonly string[] PerRequestMetadataProtocolVersions =
    [
        July2026ProtocolVersion,
    ];

    /// <summary>
    /// All protocol versions supported by this implementation.
    /// </summary>
    internal static readonly string[] SupportedProtocolVersions =
    [
        .. InitializeHandshakeProtocolVersions,
        .. PerRequestMetadataProtocolVersions,
    ];

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
    /// Returns <see langword="true"/> if the given protocol version is <see cref="July2026ProtocolVersion"/>
    /// or later, the revision that removed the <c>initialize</c> handshake and Streamable HTTP sessions.
    /// Protocol versions are ISO-8601 dates, so an ordinal comparison orders them chronologically.
    /// </summary>
    internal static bool IsJuly2026OrLaterProtocolVersion(string? protocolVersion)
        => !string.IsNullOrEmpty(protocolVersion)
            && StringComparer.Ordinal.Compare(protocolVersion, July2026ProtocolVersion) >= 0;

    /// <summary>
    /// Returns <see langword="true"/> if the given protocol version is supported by this implementation.
    /// </summary>
    internal static bool IsSupportedProtocolVersion(string? protocolVersion)
        => protocolVersion is not null && SupportedProtocolVersions.Contains(protocolVersion);

    /// <summary>
    /// Returns <see langword="true"/> if the given protocol version is available through the
    /// <c>initialize</c> handshake.
    /// </summary>
    internal static bool SupportsInitializeHandshake(string? protocolVersion)
        => protocolVersion is not null && InitializeHandshakeProtocolVersions.Contains(protocolVersion);

    /// <summary>
    /// Returns <see langword="true"/> if the given protocol version requires the handshake-free
    /// per-request metadata path.
    /// </summary>
    internal static bool RequiresPerRequestMetadata(string? protocolVersion)
        => IsJuly2026OrLaterProtocolVersion(protocolVersion);

    /// <summary>
    /// Returns <see langword="true"/> if the given protocol version requires standard MCP request headers
    /// (<c>Mcp-Method</c>, <c>Mcp-Name</c>).
    /// </summary>
    public static bool RequiresStandardHeaders(string? protocolVersion)
        => RequiresPerRequestMetadata(protocolVersion);

    /// <summary>
    /// Returns <see langword="true"/> if the given protocol version supports Streamable HTTP sessions.
    /// </summary>
    internal static bool SupportsHttpSessions(string? protocolVersion)
        => !RequiresPerRequestMetadata(protocolVersion);

    /// <summary>
    /// Returns <see langword="true"/> if the negotiated protocol version reports unresolvable
    /// resource URIs with the standard JSON-RPC <see cref="McpErrorCode.InvalidParams"/> (-32602)
    /// rather than the legacy <see cref="McpErrorCode.ResourceNotFound"/> (-32002).
    /// </summary>
    internal static bool UseInvalidParamsForMissingResource(string? protocolVersion)
        => IsJuly2026OrLaterProtocolVersion(protocolVersion);
}
