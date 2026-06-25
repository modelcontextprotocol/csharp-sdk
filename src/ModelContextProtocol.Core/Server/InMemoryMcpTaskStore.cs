using ModelContextProtocol.Protocol;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides an in-memory implementation of <see cref="IMcpTaskStore"/> for development and testing scenarios.
/// </summary>
/// <remarks>
/// <para>
/// This implementation stores all task state in memory using immutable snapshots and
/// compare-and-swap updates for thread safety without locks.
/// Tasks are not persisted across process restarts.
/// </para>
/// <para>
/// For production scenarios requiring durability, session isolation, or TTL-based cleanup,
/// implement a custom <see cref="IMcpTaskStore"/>.
/// </para>
/// </remarks>
public class InMemoryMcpTaskStore : IMcpTaskStore
{
    private readonly ConcurrentDictionary<string, McpTaskInfo> _tasks = new();

    /// <summary>
    /// Gets or sets the default poll interval in milliseconds for new tasks.
    /// </summary>
    /// <value>The default is 1000 milliseconds.</value>
    public long DefaultPollIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the default time-to-live for new tasks, or <see langword="null"/> for unlimited.
    /// </summary>
    public TimeSpan? DefaultTimeToLive { get; set; }

    /// <inheritdoc/>
    public Task<McpTaskInfo> CreateTaskAsync(CancellationToken cancellationToken = default)
    {
        var taskId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        var info = new McpTaskInfo(taskId, McpTaskStatus.Working, now, now, DefaultTimeToLive, DefaultPollIntervalMs);
        _tasks[taskId] = info;

        return Task.FromResult(info);
    }

    /// <inheritdoc/>
    public Task<McpTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        _tasks.TryGetValue(taskId, out var info);
        return Task.FromResult<McpTaskInfo?>(info);
    }

    /// <inheritdoc/>
    public Task SetCompletedAsync(string taskId, JsonElement result, CancellationToken cancellationToken = default)
    {
        Update(taskId, entry => IsTerminal(entry.Status)
            ? entry
            : entry with
            {
                Status = McpTaskStatus.Completed,
                Result = result,
                LastUpdatedAt = DateTimeOffset.UtcNow,
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetFailedAsync(string taskId, JsonElement error, CancellationToken cancellationToken = default)
    {
        Update(taskId, entry => IsTerminal(entry.Status)
            ? entry
            : entry with
            {
                Status = McpTaskStatus.Failed,
                Error = error,
                LastUpdatedAt = DateTimeOffset.UtcNow,
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> SetCancelledAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(taskId, out var entry) || IsTerminal(entry.Status))
        {
            return Task.FromResult(false);
        }

        bool transitioned = false;
        Update(taskId, e =>
        {
            if (IsTerminal(e.Status))
            {
                return e;
            }

            transitioned = true;
            return e with { Status = McpTaskStatus.Cancelled, LastUpdatedAt = DateTimeOffset.UtcNow };
        });

        return Task.FromResult(transitioned);
    }

    /// <inheritdoc/>
    public event Action<InputResponseReceivedEventArgs>? InputResponseReceived;

    /// <inheritdoc/>
    public Task ResolveInputRequestsAsync(
        string taskId,
        IDictionary<string, InputResponse> inputResponses,
        CancellationToken cancellationToken = default)
    {
        bool wasTerminal = false;
        Update(taskId, entry =>
        {
            if (IsTerminal(entry.Status))
            {
                wasTerminal = true;
                return entry;
            }

            var requests = entry.InputRequests as ImmutableDictionary<string, InputRequest>
                ?? entry.InputRequests?.ToImmutableDictionary()
                ?? ImmutableDictionary<string, InputRequest>.Empty;

            foreach (var key in inputResponses.Keys)
            {
                requests = requests.Remove(key);
            }

            var status = requests.IsEmpty ? McpTaskStatus.Working : entry.Status;

            return entry with
            {
                InputRequests = requests,
                Status = status,
                LastUpdatedAt = DateTimeOffset.UtcNow,
            };
        });

        if (wasTerminal)
        {
            // Drop responses targeting a terminal task — there are no listeners that can act on them.
            return Task.CompletedTask;
        }

        foreach (var kvp in inputResponses)
        {
            InputResponseReceived?.Invoke(new InputResponseReceivedEventArgs
            {
                TaskId = taskId,
                RequestId = kvp.Key,
                Response = kvp.Value,
            });
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetInputRequestsAsync(
        string taskId,
        IDictionary<string, InputRequest> inputRequests,
        CancellationToken cancellationToken = default)
    {
        Update(taskId, entry =>
        {
            if (IsTerminal(entry.Status))
            {
                return entry;
            }

            var requests = entry.InputRequests as ImmutableDictionary<string, InputRequest>
                ?? entry.InputRequests?.ToImmutableDictionary()
                ?? ImmutableDictionary<string, InputRequest>.Empty;

            foreach (var kvp in inputRequests)
            {
                requests = requests.SetItem(kvp.Key, kvp.Value);
            }

            return entry with
            {
                InputRequests = requests,
                Status = McpTaskStatus.InputRequired,
                LastUpdatedAt = DateTimeOffset.UtcNow,
            };
        });

        return Task.CompletedTask;
    }

    private static bool IsTerminal(McpTaskStatus status) =>
        status is McpTaskStatus.Completed or McpTaskStatus.Failed or McpTaskStatus.Cancelled;

    private void Update(string taskId, Func<McpTaskInfo, McpTaskInfo> transform)
    {
        SpinWait spin = default;
        while (true)
        {
            if (!_tasks.TryGetValue(taskId, out var current))
            {
                throw new InvalidOperationException($"Task '{taskId}' not found.");
            }

            var updated = transform(current);
            if (ReferenceEquals(updated, current) || _tasks.TryUpdate(taskId, updated, current))
            {
                return;
            }

            spin.SpinOnce();
        }
    }
}
