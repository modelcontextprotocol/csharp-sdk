namespace ModelContextProtocol.Server;

/// <summary>
/// Optional contract for tools that expose a per-tool execution timeout.
/// </summary>
/// <remarks>
/// When specified, this value overrides the server-level
/// <see cref="McpServerOptions.DefaultToolTimeout"/> for this tool only.
/// </remarks>
public interface IMcpToolWithTimeout
{
    /// <summary>
    /// Gets the per-tool timeout. When <see langword="null"/>, the server's
    /// default applies (if any).
    /// </summary>
    TimeSpan? Timeout { get; }
}