using System.Collections.Concurrent;

namespace ModelContextProtocol;

/// <summary>
/// Provides cancellation tokens for running MCP tasks, enabling TTL-based
/// automatic cancellation and explicit task cancellation.
/// </summary>
/// <remarks>
/// <para>
/// This class provides lifecycle management for <see cref="CancellationTokenSource"/> instances
/// associated with running tasks. Each task gets its own CTS that can be:
/// </para>
/// <list type="bullet">
/// <item><description>Automatically cancelled when the task's TTL expires</description></item>
/// <item><description>Explicitly cancelled via the <see cref="Cancel"/> method</description></item>
/// <item><description>Cleaned up when the task completes via <see cref="Complete"/></description></item>
/// </list>
/// <para>
/// Both <c>McpClient</c> and <c>McpServer</c> use this class to manage task cancellation
/// independently of request cancellation tokens.
/// </para>
/// </remarks>
internal sealed class McpTaskCancellationTokenProvider : IDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningTasks = new();
    private bool _disposed;

    /// <summary>
    /// Registers a new task and returns a cancellation token for use during execution.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task.</param>
    /// <param name="timeToLive">
    /// Optional TTL duration. If specified, the returned token will be automatically
    /// cancelled when the TTL expires.
    /// </param>
    /// <returns>
    /// A <see cref="CancellationToken"/> that will be cancelled when the TTL expires,
    /// when <see cref="Cancel"/> is called, or when this provider is disposed.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    /// <exception cref="InvalidOperationException">A task with the same ID is already registered.</exception>
    public CancellationToken RequestToken(string taskId, TimeSpan? timeToLive)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(McpTaskCancellationTokenProvider));
        }

        Throw.IfNullOrWhiteSpace(taskId);
        CancellationTokenSource cts = new();

        if (timeToLive is { } ttl)
        {
            cts.CancelAfter(ttl);
        }

        if (!_runningTasks.TryAdd(taskId, cts))
        {
            cts.Dispose();
            throw new InvalidOperationException($"Task '{taskId}' is already registered.");
        }

        return cts.Token;
    }

    /// <summary>
    /// Attempts to cancel a running task.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task to cancel.</param>
    /// <remarks>
    /// This method signals cancellation but does not remove the task from tracking.
    /// The task executor should call <see cref="Complete"/> when it observes
    /// the cancellation and finishes cleanup.
    /// </remarks>
    public void Cancel(string taskId)
    {
        if (_runningTasks.TryGetValue(taskId, out var cts))
        {
            cts.Cancel();
        }
    }

    /// <summary>
    /// Marks a task as complete and releases its associated resources.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task that has completed.</param>
    /// <remarks>
    /// This method should be called from a <c>finally</c> block in the task execution
    /// to ensure proper cleanup regardless of success, failure, or cancellation.
    /// </remarks>
    public void Complete(string taskId)
    {
        if (_runningTasks.TryRemove(taskId, out var cts))
        {
            cts.Dispose();
        }
    }

    /// <summary>
    /// Cancels all running tasks and releases all resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var kvp in _runningTasks)
        {
            try
            {
                kvp.Value.Cancel();
                kvp.Value.Dispose();
            }
            catch
            {
                // Best effort cleanup
            }
        }

        _runningTasks.Clear();
    }
}
