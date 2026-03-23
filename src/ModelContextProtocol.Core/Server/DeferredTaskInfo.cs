using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Holds the state needed for deferred task creation, where a tool handler performs
/// ephemeral MRTR exchanges before committing to a background task via
/// <see cref="McpServer.CreateTaskAsync(CancellationToken)"/>.
/// Stored on <see cref="MrtrContext.DeferredTask"/> and carried across MRTR continuations.
/// </summary>
internal sealed class DeferredTaskInfo
{
    /// <summary>Gets the task metadata from the original client request.</summary>
    public required McpTaskMetadata TaskMetadata { get; init; }

    /// <summary>Gets the JSON-RPC request ID of the current tools/call request.</summary>
    public required RequestId OriginalRequestId { get; init; }

    /// <summary>Gets the original JSON-RPC request.</summary>
    public required JsonRpcRequest OriginalRequest { get; init; }

    /// <summary>Gets the task store for persisting task state.</summary>
    public required IMcpTaskStore TaskStore { get; init; }

    /// <summary>Gets whether to send task status notifications.</summary>
    public required bool SendNotifications { get; init; }

    /// <summary>
    /// Task that completes when the handler calls <see cref="McpServer.CreateTaskAsync(CancellationToken)"/>.
    /// The framework races this against handler completion and MRTR exchanges.
    /// </summary>
    private readonly TaskCompletionSource<bool> _signalTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// TCS that the framework completes after creating the task, allowing the handler to continue.
    /// </summary>
    private readonly TaskCompletionSource<DeferredTaskCreationResult> _ackTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets the task that completes when the handler requests task creation.</summary>
    public Task SignalTask => _signalTcs.Task;

    /// <summary>
    /// Called by the handler (via <see cref="McpServer.CreateTaskAsync(CancellationToken)"/>) to signal
    /// the framework that a task should be created. Awaits the framework's acknowledgment.
    /// </summary>
    /// <returns>The result containing the created task's context information.</returns>
    /// <exception cref="InvalidOperationException"><see cref="McpServer.CreateTaskAsync(CancellationToken)"/> was already called.</exception>
    public async ValueTask<DeferredTaskCreationResult> RequestTaskCreationAsync(CancellationToken cancellationToken)
    {
        if (!_signalTcs.TrySetResult(true))
        {
            throw new InvalidOperationException("CreateTaskAsync has already been called for this tool execution.");
        }

        return await _ackTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Called by the framework after creating the task to unblock the handler.
    /// </summary>
    /// <exception cref="InvalidOperationException">Task creation was already acknowledged.</exception>
    public void AcknowledgeTaskCreation(DeferredTaskCreationResult result)
    {
        if (!_ackTcs.TrySetResult(result))
        {
            throw new InvalidOperationException("Task creation was already acknowledged.");
        }
    }

    /// <summary>
    /// Called by the framework when task creation fails, propagating the exception
    /// to the handler so <see cref="McpServer.CreateTaskAsync(CancellationToken)"/> throws.
    /// </summary>
    public void AcknowledgeFailure(Exception exception)
    {
        _ackTcs.TrySetException(exception);
    }
}
