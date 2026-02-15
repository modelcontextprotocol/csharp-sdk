namespace ModelContextProtocol.Protocol;

/// <summary>
/// Provides the protocol versions supported by this implementation of MCP.
/// </summary>
public static class ProtocolVersions
{
    /// <summary>
    /// Gets the protocol versions supported by this implementation of MCP.
    /// </summary>
    public static IReadOnlyList<string> SupportedVersions { get; } = McpSessionHandler.SupportedProtocolVersions;
}
