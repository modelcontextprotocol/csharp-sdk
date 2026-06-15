namespace ModelContextProtocol.Protocol;

/// <summary>
/// Provides constants for well-known MCP extension identifiers.
/// </summary>
public static class McpExtensions
{
    /// <summary>
    /// The extension identifier for the MCP Tasks extension.
    /// </summary>
    /// <remarks>
    /// When included in client per-request capabilities, indicates the client can handle
    /// <see cref="CreateTaskResult"/> in lieu of a standard result.
    /// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md">SEP-2663</see>
    /// specification for details.
    /// </remarks>
    public const string Tasks = "io.modelcontextprotocol/tasks";
}
