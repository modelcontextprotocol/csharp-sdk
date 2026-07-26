using ModelContextProtocol.Protocol;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;

namespace ModelContextProtocol.Extensions.Tasks;

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
/// Tasks created with a <see cref="DefaultTimeToLive"/> are discarded once their time-to-live
/// elapses (as permitted by SEP-2663): an expired task is removed on access, and an opportunistic
/// throttled sweep reclaims expired tasks that are never polled again. Tasks created without a
/// time-to-live are retained until the process exits.
/// </para>
/// <para>
/// For production scenarios requiring durability, session isolation, or more advanced retention
/// policies, implement a custom <see cref="IMcpTaskStore"/>.
/// </para>
/// </remarks>
public class InMemoryMcpTaskStore : IMcpTaskStore
{
    private readonly ConcurrentDictionary<string, McpTaskInfo> _tasks = new();
    private static readonly long s_sweepIntervalTicks = TimeSpan.FromSeconds(30).Ticks;
    private long _lastSweepTicks = DateTimeOffset.UtcNow.UtcTicks;

    /// <summary>
    /// Gets or sets the default poll interval in milliseconds for new tasks.
    /// </summary>
    /// <value>The default is 1000 milliseconds.</value>
    public long DefaultPollIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the default time-to-live for new tasks, or <see langword="null"/> for unlimited.
    /// </summary>
    /// <remarks>
    /// When set to a positive value, tasks are discarded once this duration elapses from their
    /// creation. A <see langword="null"/> or non-positive value keeps tasks until the process exits.
    /// </remarks>
    public TimeSpan? DefaultTimeToLive { get; set; }

    /// <inheritdoc/>
    public Task<McpTaskInfo> CreateTaskAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        SweepExpired(now);

        var taskId = Guid.NewGuid().ToString("N");

        var info = new McpTaskInfo(taskId, McpTaskStatus.Working, now, now, DefaultTimeToLive, DefaultPollIntervalMs);
        _tasks[taskId] = info;

        return Task.FromResult(info);
    }

    /// <inheritdoc/>
    public Task<McpTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (_tasks.TryGetValue(taskId, out var info))
        {
            if (IsExpired(info, now))
            {
                _tasks.TryRemove(taskId, out _);
                return Task.FromResult<McpTaskInfo?>(null);
            }

            return Task.FromResult<McpTaskInfo?>(info);
        }

        return Task.FromResult<McpTaskInfo?>(null);
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

    private static bool IsExpired(McpTaskInfo info, DateTimeOffset now) =>
        info.TimeToLive is { } ttl && ttl > TimeSpan.Zero && now - info.CreatedAt >= ttl;

    private void SweepExpired(DateTimeOffset now)
    {
        long last = Interlocked.Read(ref _lastSweepTicks);
        if (now.UtcTicks - last < s_sweepIntervalTicks)
        {
            return;
        }

        // Ensure only one caller runs the sweep per interval; concurrent callers skip it.
        if (Interlocked.CompareExchange(ref _lastSweepTicks, now.UtcTicks, last) != last)
        {
            return;
        }

        foreach (var kvp in _tasks)
        {
            if (IsExpired(kvp.Value, now))
            {
                // TaskId values are unique GUIDs that are never reused, and CreatedAt/TimeToLive
                // are immutable after creation, so an entry judged expired here stays expired.
                // Removing by key is therefore safe even if the value was concurrently updated.
                _tasks.TryRemove(kvp.Key, out _);
            }
        }
    }

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
