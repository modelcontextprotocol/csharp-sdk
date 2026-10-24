using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Extensions.Tasks;

/// <summary>
/// Extension methods for task-aware operations on <see cref="McpClientTool"/> instances.
/// </summary>
public static class McpTasksClientToolExtensions
{
    /// <summary>
    /// Calls a tool and returns either an immediate result or a created task.
    /// </summary>
    /// <param name="tool">The tool to invoke.</param>
    /// <param name="arguments">An optional dictionary of arguments to pass to the tool.</param>
    /// <param name="options">Optional request options including metadata and serialization settings.</param>
    /// <param name="cancellationToken">The cancellation token to monitor.</param>
    /// <returns>The immediate tool result or information about the created task.</returns>
    public static ValueTask<ResultOrCreatedTask<CallToolResult>> CallAsTaskAsync(
        this McpClientTool tool,
        IReadOnlyDictionary<string, object?>? arguments = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
#if NET
        ArgumentNullException.ThrowIfNull(tool);
#else
        if (tool is null) throw new ArgumentNullException(nameof(tool));
#endif

        return tool.Client.CallToolAsTaskAsync(
            tool.CreateCallToolRequestParams(arguments, options),
            cancellationToken);
    }

    /// <summary>
    /// Calls a tool and, if the server creates a task, polls it to completion.
    /// </summary>
    /// <param name="tool">The tool to invoke.</param>
    /// <param name="arguments">An optional dictionary of arguments to pass to the tool.</param>
    /// <param name="options">Optional request options including metadata and serialization settings.</param>
    /// <param name="maxConsecutiveStuckPolls">
    /// The maximum number of consecutive polls that may report input required without publishing a new input request.
    /// </param>
    /// <param name="cancellationToken">The cancellation token to monitor.</param>
    /// <returns>The completed tool result.</returns>
    public static ValueTask<CallToolResult> CallWithPollingAsync(
        this McpClientTool tool,
        IReadOnlyDictionary<string, object?>? arguments = null,
        RequestOptions? options = null,
        int maxConsecutiveStuckPolls = 60,
        CancellationToken cancellationToken = default)
    {
#if NET
        ArgumentNullException.ThrowIfNull(tool);
#else
        if (tool is null) throw new ArgumentNullException(nameof(tool));
#endif

        return tool.Client.CallToolWithPollingAsync(
            tool.CreateCallToolRequestParams(arguments, options),
            maxConsecutiveStuckPolls,
            cancellationToken);
    }
}
