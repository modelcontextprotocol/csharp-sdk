namespace ModelContextProtocol.Extensions.Tasks;

/// <summary>
/// Specifies how a tool call participates in the MCP Tasks extension.
/// </summary>
public enum McpTaskExecutionMode
{
    /// <summary>The tool always executes synchronously.</summary>
    Synchronous,

    /// <summary>The tool executes as a task when the client declares the Tasks extension.</summary>
    Optional,

    /// <summary>The tool requires the client to declare the Tasks extension.</summary>
    Required,
}
