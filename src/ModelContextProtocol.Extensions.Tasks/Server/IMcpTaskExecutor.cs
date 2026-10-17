using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

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
/// Executors that delegate to an external runtime should also implement
/// <see cref="CreateExecutionIntentAsync"/> so a persisted intent is available for
/// recovering tasks whose process exited between task creation and a completed start.
/// See that method for the intent contract.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md">SEP-2663</see>
/// specification for details on the tasks extension.
/// </para>
/// </remarks>
public interface IMcpTaskExecutor
{
    /// <summary>
    /// Creates a portable, side-effect-free description of how the task will be started,
    /// which the SDK persists atomically with the task record.
    /// </summary>
    /// <param name="request">The tool request that is being converted into a task.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// An opaque <see cref="JsonElement"/> carrying the execution intent, or <see langword="null"/>
    /// for stateless executors (such as <see cref="ProcessLocalMcpTaskExecutor"/>) that need no
    /// recovery metadata.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The intent closes the crash window between task creation and start: the store persists it
    /// atomically alongside the task record, so if the process exits before
    /// <see cref="StartAsync"/> completes, an integration can discover the orphaned task through
    /// its own durable store and reconstruct the submission from the persisted intent — using the
    /// task ID as an idempotency key so a resubmission is safe.
    /// </para>
    /// <para>
    /// The intent is executor-owned and opaque to the SDK: its schema and versioning belong to
    /// the executor, not to the SDK or the protocol. It must be portable — only data the external
    /// runtime needs to reconstruct the submission, such as the tool name, arguments, and
    /// executor-specific routing hints. It must not capture runtime objects, services,
    /// credentials, clients, transports, or delegates. Because the intent is persisted and may
    /// be recovered by a later deployment of the executor, embed a version marker in the payload
    /// so reconciliation code can distinguish generations and evolve safely.
    /// </para>
    /// <para>
    /// This method must be side-effect-free: no external submission or state mutation happens
    /// here. It runs after authorization and validation, but before the task record is created.
    /// If it throws, no task is created and the exception fails the original <c>tools/call</c>.
    /// </para>
    /// <para>
    /// The persisted intent is server-only: it never surfaces in MCP responses, notifications,
    /// or errors.
    /// </para>
    /// </remarks>
    ValueTask<JsonElement?> CreateExecutionIntentAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken);

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
