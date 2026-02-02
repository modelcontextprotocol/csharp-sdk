using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace LongRunningTasks.Tools;

/// <summary>
/// Demonstrates creating and returning tasks via <see cref="IMcpTaskStore"/>.
/// </summary>
[McpServerToolType]
public class TaskTools(IMcpTaskStore taskStore)
{
    /// <summary>
    /// Submits a job to the task store and returns a task handle for polling.
    /// </summary>
    [McpServerTool]
    [Description("Submits a job and returns a task that can be polled for completion.")]
    public Task<McpTask> SubmitJob(
        [Description("A label for the job")] string jobName,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        return taskStore.CreateTaskAsync(
            new McpTaskMetadata { TimeToLive = TimeSpan.FromMinutes(10) },
            context.JsonRpcRequest.Id!,
            context.JsonRpcRequest,
            context.Server.SessionId,
            cancellationToken);
    }
}
