using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Extensions.Tasks;

/// <summary>
/// Executes a task created by the Tasks extension after the task record has been
/// durably created in the <see cref="IMcpTaskStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// By default, tasks execute in-process via the .NET thread pool. Registering a custom
/// executor delegates execution to an external system such as Temporal, Orleans, Hangfire,
/// or a distributed queue. The executor is invoked once per task, after the task record is
/// created and the execution context is fully wired.
/// </para>
/// <para>
/// <see cref="StartAsync"/> must return only after execution has been durably started
/// (e.g., the external runtime has accepted the job), mirroring the durability requirement
/// SEP-2663 §306 places on <see cref="IMcpTaskStore.CreateTaskAsync"/>. It must not wait
/// for the task to complete; completion is recorded in the store by whichever system
/// performs the execution.
/// </para>
/// <para>
/// If <see cref="StartAsync"/> throws, the exception is not returned as an error from the
/// original <c>tools/call</c>: that call still succeeds with <see cref="CreateTaskResult"/>,
/// the task is marked failed via <see cref="IMcpTaskStore.SetFailedAsync"/>, and the client
/// discovers the failure on its first poll. After a successful <see cref="StartAsync"/>,
/// the SDK no longer tracks the task; the store is the single source of truth for its state.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md">SEP-2663</see>
/// specification for details on the tasks extension.
/// </para>
/// </remarks>
public interface IMcpTaskExecutor
{
    /// <summary>
    /// Starts execution of a task.
    /// </summary>
    /// <param name="context">
    /// The execution context for the task, providing the task identity, the matched tool
    /// request bound to a fresh execution scope, and a helper for running the normal tool
    /// invocation pipeline locally.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that fires when the task is cancelled via <c>tasks/cancel</c>.
    /// </param>
    /// <returns>A <see cref="ValueTask"/> that completes when execution has been durably started.</returns>
    ValueTask StartAsync(McpTaskExecutionContext context, CancellationToken cancellationToken);
}
