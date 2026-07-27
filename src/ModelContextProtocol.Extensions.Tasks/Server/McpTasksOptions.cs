using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Extensions.Tasks;

/// <summary>
/// Configures server-side MCP Tasks behavior.
/// </summary>
public sealed class McpTasksOptions
{
    /// <summary>
    /// Gets or sets the callback that selects task execution behavior for each tool call.
    /// </summary>
    /// <remarks>
    /// The default treats every tool as task-capable, preserving the behavior of
    /// <c>WithTasks</c>.
    /// The callback may inspect <see cref="RequestContext{TParams}.MatchedPrimitive"/> to select
    /// behavior from tool metadata without requiring Core to reference the Tasks extension.
    /// </remarks>
    public Func<RequestContext<CallToolRequestParams>, McpTaskExecutionMode> ExecutionModeSelector { get; set; } =
        static _ => McpTaskExecutionMode.Optional;
}
