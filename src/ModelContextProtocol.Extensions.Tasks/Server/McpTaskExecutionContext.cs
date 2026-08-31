using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Extensions.Tasks;

/// <summary>
/// The execution context handed to an <see cref="IMcpTaskExecutor"/> when a task starts.
/// </summary>
/// <remarks>
/// <para>
/// The context owns the request-scoped services and the cancellation registration for task
/// execution. Executors that run the tool locally via <see cref="RunToolPipelineAsync"/>
/// never dispose anything explicitly; the context releases its resources when the pipeline
/// completes. Executors that hand execution off to an external system should extract what
/// they need from <see cref="Request"/> and then call <see cref="DisposeAsync"/> once the
/// scope-bound services are no longer needed.
/// </para>
/// <para>
/// <see cref="Request"/> carries a server whose outgoing requests (elicitation, sampling)
/// are intercepted and routed through the task's pending input requests, so responses
/// submitted via <c>tasks/update</c> are delivered even when a different server instance
/// serves the polling client.
/// </para>
/// <para>
/// <see cref="RunToolPipelineAsync"/> and <see cref="DisposeAsync"/> coordinate through an
/// atomic state transition: concurrent callers cannot both win, so the pipeline runs at most
/// once and disposal cannot race with it. Every caller but the winner observes the context
/// as disposed.
/// </para>
/// </remarks>
public sealed class McpTaskExecutionContext : IAsyncDisposable
{
    private readonly Func<RequestContext<CallToolRequestParams>, CancellationToken, Task> _pipelineRunner;
    private readonly Func<Task> _disposer;
    private int _disposed;

    internal McpTaskExecutionContext(
        McpTaskInfo taskInfo,
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellation,
        Func<RequestContext<CallToolRequestParams>, CancellationToken, Task> pipelineRunner,
        Func<Task> disposer)
    {
        TaskInfo = taskInfo;
        Request = request;
        CancellationToken = cancellation;
        _pipelineRunner = pipelineRunner;
        _disposer = disposer;
    }

    /// <summary>
    /// Gets the unique identifier of the created task.
    /// </summary>
    public string TaskId => TaskInfo.TaskId;

    /// <summary>
    /// Gets the store record for the created task, with an initial status of
    /// <see cref="McpTaskStatus.Working"/>.
    /// </summary>
    public McpTaskInfo TaskInfo { get; }

    /// <summary>
    /// Gets the matched tool request, bound to the task's execution scope, with the task
    /// outgoing-request interceptor already attached.
    /// </summary>
    public RequestContext<CallToolRequestParams> Request { get; }

    /// <summary>
    /// Gets a token that fires when the task is cancelled via <c>tasks/cancel</c>.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Runs the normal tool invocation pipeline (the remaining request filters and the tool
    /// itself), records the outcome in the task store, and releases the context's resources.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this when the executor wants the tool to run locally, preserving the behavior of
    /// <c>WithTasks</c> without a custom executor. Outcomes — including cancellation, protocol
    /// errors, and unhandled exceptions — are recorded in the store by this call; it does not
    /// rethrow them.
    /// </para>
    /// <para>
    /// After calling this method, the executor must not use <see cref="Request"/> again, and
    /// <see cref="DisposeAsync"/> becomes a no-op.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel pipeline execution.</param>
    public async ValueTask RunToolPipelineAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            throw new ObjectDisposedException(nameof(McpTaskExecutionContext));
        }

        await _pipelineRunner(Request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Releases the context's resources: the request-scoped services of the execution scope
    /// and the cancellation registration for the task.
    /// </summary>
    /// <remarks>
    /// Executors that hand execution off to an external system call this once they no longer
    /// need <see cref="Request"/>. It is called automatically when
    /// <see cref="RunToolPipelineAsync"/> completes; calling it afterwards is a no-op.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        await _disposer().ConfigureAwait(false);
    }
}
