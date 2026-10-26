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
/// Tasks created with a <see cref="DefaultTimeToLive"/> use two clocks (as permitted by SEP-2663):
/// a work window of <c>CreatedAt + TimeToLive</c> during which input and progress are accepted,
/// and a tracking window that keeps a never-terminal snapshot visible until
/// <c>CreatedAt + 2×TimeToLive</c> so a late complete, fail, or cancel can still be recorded.
/// A terminal snapshot stays visible until <c>LastUpdatedAt + TimeToLive</c>, or until
/// <see cref="GetTaskAsync"/> serves it and then drops the row. An opportunistic throttled
/// sweep reclaims snapshots nobody collected. Tasks created without a time-to-live are
/// retained until a terminal <see cref="GetTaskAsync"/> or until the process exits.
/// </para>
/// <para>
/// For production scenarios requiring durability, session isolation, or more advanced retention
/// policies, implement a custom <see cref="IMcpTaskStore"/>.
/// </para>
/// </remarks>
public class InMemoryMcpTaskStore : IMcpTaskStore
{
    internal readonly ConcurrentDictionary<string, McpTaskInfo> _tasks = new();
    internal readonly TimeProvider _timeProvider;
    internal static readonly long s_sweepIntervalTicks = TimeSpan.FromSeconds(30).Ticks;
    internal long _lastSweepTicks;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryMcpTaskStore"/> class using <see cref="TimeProvider.System"/>.
    /// </summary>
    public InMemoryMcpTaskStore()
        : this(TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryMcpTaskStore"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider used for timestamps, time-to-live, and sweep throttling.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    public InMemoryMcpTaskStore(TimeProvider timeProvider)
    {
#if NET
        ArgumentNullException.ThrowIfNull(timeProvider);
#else
        if (timeProvider is null)
        {
            throw new ArgumentNullException(nameof(timeProvider));
        }
#endif

        _timeProvider = timeProvider;
        _lastSweepTicks = timeProvider.GetUtcNow().UtcTicks;
    }

    /// <summary>
    /// Gets or sets the default poll interval in milliseconds for new tasks.
    /// </summary>
    /// <value>The default is 1000 milliseconds.</value>
    public long DefaultPollIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the default time-to-live for new tasks, or <see langword="null"/> for unlimited.
    /// </summary>
    /// <remarks>
    /// When set to a positive value, the work window ends at <c>CreatedAt + TimeToLive</c> and a
    /// never-terminal snapshot is discarded at <c>CreatedAt + 2×TimeToLive</c>. A terminal snapshot
    /// is discarded at <c>LastUpdatedAt + TimeToLive</c>, or sooner when <see cref="GetTaskAsync"/>
    /// serves it. A <see langword="null"/> or non-positive value keeps tasks until a terminal get
    /// or until the process exits.
    /// </remarks>
    public TimeSpan? DefaultTimeToLive { get; set; }

    /// <inheritdoc/>
    public Task<McpTaskInfo> CreateTaskAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = _timeProvider.GetUtcNow();
        var taskId = Guid.NewGuid().ToString("N");
        var info = new McpTaskInfo(taskId, McpTaskStatus.Working, now, now, DefaultTimeToLive, DefaultPollIntervalMs);
        _tasks[taskId] = info;

        SweepExpired(now, cancellationToken);

        return Task.FromResult(info);
    }

    /// <inheritdoc/>
    public Task<McpTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = _timeProvider.GetUtcNow();
        SweepExpired(now, cancellationToken);

        if (!_tasks.TryGetValue(taskId, out var info))
        {
            return Task.FromResult<McpTaskInfo?>(null);
        }

        if (HasExpired(info, now))
        {
            _tasks.TryRemove(taskId, out _);
            return Task.FromResult<McpTaskInfo?>(null);
        }

        if (IsTerminal(info.Status))
        {
            // The C# client polls tasks/get until it sees a terminal snapshot, then stops.
            // Serving that snapshot and dropping the row is the primary prune.
            _tasks.TryRemove(taskId, out _);
        }

        return Task.FromResult<McpTaskInfo?>(info);
    }

    /// <inheritdoc/>
    public Task SetCompletedAsync(string taskId, JsonElement result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _timeProvider.GetUtcNow();

        TryUpdate(taskId, entry =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsTerminal(entry.Status) || HasExpired(entry, now))
            {
                return entry;
            }

            return entry with
            {
                Status = McpTaskStatus.Completed,
                Result = result,
                LastUpdatedAt = now,
            };
        }, cancellationToken);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetFailedAsync(string taskId, JsonElement error, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _timeProvider.GetUtcNow();

        TryUpdate(taskId, entry =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsTerminal(entry.Status) || HasExpired(entry, now))
            {
                return entry;
            }

            return entry with
            {
                Status = McpTaskStatus.Failed,
                Error = error,
                LastUpdatedAt = now,
            };
        }, cancellationToken);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> SetCancelledAsync(string taskId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _timeProvider.GetUtcNow();

        bool transitioned = false;
        if (!TryUpdate(taskId, e =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsTerminal(e.Status) || HasExpired(e, now))
            {
                transitioned = false;
                return e;
            }

            transitioned = true;
            return e with { Status = McpTaskStatus.Cancelled, LastUpdatedAt = now };
        }, cancellationToken))
        {
            return Task.FromResult(false);
        }

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
        cancellationToken.ThrowIfCancellationRequested();
        var now = _timeProvider.GetUtcNow();

        bool accepted = false;
        TryUpdate(taskId, entry =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsActive(entry, now))
            {
                accepted = false;
                return entry;
            }

            var requests = GetRequests(entry);
            foreach (var key in inputResponses.Keys)
            {
                requests = requests.Remove(key);
            }

            var status = requests.IsEmpty ? McpTaskStatus.Working : entry.Status;
            accepted = true;

            return entry with
            {
                InputRequests = requests,
                Status = status,
                LastUpdatedAt = now,
            };
        }, cancellationToken);

        if (!accepted)
        {
            // Missing, expired, second-window, or already terminal: no listeners can act on these responses.
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
        cancellationToken.ThrowIfCancellationRequested();
        var now = _timeProvider.GetUtcNow();

        TryUpdate(taskId, entry =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsActive(entry, now))
            {
                return entry;
            }

            var requests = GetRequests(entry);
            foreach (var kvp in inputRequests)
            {
                requests = requests.SetItem(kvp.Key, kvp.Value);
            }

            return entry with
            {
                InputRequests = requests,
                Status = McpTaskStatus.InputRequired,
                LastUpdatedAt = now,
            };
        }, cancellationToken);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns the task's pending input requests as an <see cref="ImmutableDictionary{TKey, TValue}"/>.
    /// </summary>
    /// <param name="entry">The task snapshot whose requests should be read.</param>
    /// <returns>
    /// <paramref name="entry"/>'s <see cref="McpTaskInfo.InputRequests"/> when it is already an
    /// immutable dictionary; a copy when it is a different dictionary type; or an empty dictionary
    /// when no requests are stored.
    /// </returns>
    internal static ImmutableDictionary<string, InputRequest> GetRequests(McpTaskInfo entry) =>
        entry.InputRequests as ImmutableDictionary<string, InputRequest>
        ?? entry.InputRequests?.ToImmutableDictionary()
        ?? ImmutableDictionary<string, InputRequest>.Empty;

    /// <summary>
    /// Returns whether <paramref name="status"/> is a terminal task status.
    /// </summary>
    /// <param name="status">The status to inspect.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="status"/> is
    /// <see cref="McpTaskStatus.Completed"/>, <see cref="McpTaskStatus.Failed"/>, or
    /// <see cref="McpTaskStatus.Cancelled"/>; otherwise <see langword="false"/>.
    /// </returns>
    internal static bool IsTerminal(McpTaskStatus status) =>
        status is McpTaskStatus.Completed or McpTaskStatus.Failed or McpTaskStatus.Cancelled;

    /// <summary>
    /// Returns whether the task is still in its work window and may accept input, progress, or settle.
    /// </summary>
    /// <param name="info">The task snapshot to inspect.</param>
    /// <param name="now">The current UTC time from the store's <see cref="TimeProvider"/>.</param>
    /// <returns>
    /// <see langword="false"/> when the task is terminal. Otherwise <see langword="true"/> when the
    /// task has no positive time-to-live, or when <paramref name="now"/> is still earlier than
    /// <c>CreatedAt + TimeToLive</c>.
    /// </returns>
    internal static bool IsActive(McpTaskInfo info, DateTimeOffset now)
    {
        if (IsTerminal(info.Status))
        {
            return false;
        }

        if (!TryGetPositiveTtl(info, out var ttl))
        {
            return true;
        }

        return now - info.CreatedAt < ttl;
    }

    /// <summary>
    /// Returns whether the snapshot has no remaining reads and should be discarded.
    /// </summary>
    /// <param name="info">The task snapshot to inspect.</param>
    /// <param name="now">The current UTC time from the store's <see cref="TimeProvider"/>.</param>
    /// <returns>
    /// <see langword="false"/> when the task has no positive time-to-live.
    /// For a terminal snapshot, <see langword="true"/> when <paramref name="now"/> is at or past
    /// <c>LastUpdatedAt + TimeToLive</c>. For a never-terminal snapshot, <see langword="true"/> when
    /// <paramref name="now"/> is at or past <c>CreatedAt + 2×TimeToLive</c>.
    /// </returns>
    internal static bool HasExpired(McpTaskInfo info, DateTimeOffset now)
    {
        if (!TryGetPositiveTtl(info, out var ttl))
        {
            return false;
        }

        if (IsTerminal(info.Status))
        {
            return now - info.LastUpdatedAt >= ttl;
        }

        var elapsed = now - info.CreatedAt;
        return elapsed >= ttl && elapsed - ttl >= ttl;
    }

    /// <summary>
    /// Tries to read a positive time-to-live from <paramref name="info"/>.
    /// </summary>
    /// <param name="info">The task snapshot whose time-to-live should be read.</param>
    /// <param name="ttl">
    /// When this method returns <see langword="true"/>, the positive time-to-live.
    /// When this method returns <see langword="false"/>, <see cref="TimeSpan.Zero"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <see cref="McpTaskInfo.TimeToLive"/> is a positive duration;
    /// otherwise <see langword="false"/> (null, zero, or negative means unlimited retention).
    /// </returns>
    internal static bool TryGetPositiveTtl(McpTaskInfo info, out TimeSpan ttl)
    {
        if (info.TimeToLive is { } value && value > TimeSpan.Zero)
        {
            ttl = value;
            return true;
        }

        ttl = default;
        return false;
    }

    /// <summary>
    /// Removes snapshots for which <see cref="HasExpired"/> is <see langword="true"/>, at most once
    /// per <see cref="s_sweepIntervalTicks"/>.
    /// </summary>
    /// <param name="now">The current UTC time used for expiry and throttle checks.</param>
    /// <param name="cancellationToken">
    /// Observed at each dictionary entry so a cancelled caller does not finish the walk.
    /// </param>
    /// <remarks>
    /// Create inserts the new snapshot before calling this method, and <see cref="GetTaskAsync"/>
    /// pulses it so unpolled rows are still reclaimed. Concurrent callers skip the walk when another
    /// thread already claimed the interval. Task ids are never reused, so removing by key is safe
    /// even if a value was updated after it was judged expired.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled during the walk.
    /// </exception>
    internal void SweepExpired(DateTimeOffset now, CancellationToken cancellationToken)
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
            cancellationToken.ThrowIfCancellationRequested();
            if (HasExpired(kvp.Value, now))
            {
                // TaskId values are unique GUIDs that are never reused. HasExpired is monotonic
                // for a given snapshot (CreatedAt/TimeToLive are immutable; LastUpdatedAt only
                // moves forward on settle), so an entry judged expired here stays expired.
                _tasks.TryRemove(kvp.Key, out _);
            }
        }
    }

    /// <summary>
    /// Applies <paramref name="transform"/> to the snapshot for <paramref name="taskId"/> using
    /// compare-and-swap until the write succeeds or the key is gone.
    /// </summary>
    /// <param name="taskId">The task identifier to update.</param>
    /// <param name="transform">
    /// Receives the current snapshot and returns either that same instance (no-op) or a replacement.
    /// </param>
    /// <param name="cancellationToken">Observed before each read so a cancelled caller does not commit.</param>
    /// <returns>
    /// <see langword="true"/> when the key existed and the transform was applied or was a no-op;
    /// <see langword="false"/> when the key is missing.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled before a commit.
    /// </exception>
    internal bool TryUpdate(string taskId, Func<McpTaskInfo, McpTaskInfo> transform, CancellationToken cancellationToken)
    {
        SpinWait spin = default;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_tasks.TryGetValue(taskId, out var current))
            {
                return false;
            }

            var updated = transform(current);
            if (ReferenceEquals(updated, current) || _tasks.TryUpdate(taskId, updated, current))
            {
                return true;
            }

            spin.SpinOnce();
        }
    }
}
