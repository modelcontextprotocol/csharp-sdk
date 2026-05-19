using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Contains the information the handler needs after the framework creates the deferred task.
/// </summary>
internal sealed class DeferredTaskCreationResult
{
    /// <summary>Gets the ID of the created task.</summary>
    public required string TaskId { get; init; }

    /// <summary>Gets the session ID associated with the task.</summary>
    public required string? SessionId { get; init; }

    /// <summary>Gets the task store for persisting task state.</summary>
    public required IMcpTaskStore TaskStore { get; init; }

    /// <summary>Gets whether to send task status notifications.</summary>
    public required bool SendNotifications { get; init; }

    /// <summary>Gets the function for sending task status notifications.</summary>
    public required Func<McpTask, CancellationToken, Task>? NotifyTaskStatusFunc { get; init; }

    /// <summary>Gets the cancellation token for the task (TTL-based or explicit).</summary>
    public required CancellationToken TaskCancellationToken { get; init; }
}
