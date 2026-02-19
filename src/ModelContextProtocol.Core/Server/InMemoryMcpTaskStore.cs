using ModelContextProtocol.Protocol;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

#if MCP_TEST_TIME_PROVIDER
namespace ModelContextProtocol.Tests.Internal;
#else
namespace ModelContextProtocol;
#endif

/// <summary>
/// Provides an in-memory implementation of <see cref="IMcpTaskStore"/> for development and testing.
/// </summary>
/// <remarks>
/// <para>
/// This implementation uses thread-safe concurrent collections and is suitable for single-server
/// scenarios and testing. It is not recommended for production multi-server deployments as tasks
/// are stored only in memory and are lost on server restart.
/// </para>
/// <para>
/// Features:
/// <list type="bullet">
/// <item><description>Thread-safe operations using <see cref="ConcurrentDictionary{TKey, TValue}"/></description></item>
/// <item><description>Automatic TTL-based cleanup via background task</description></item>
/// <item><description>Session-based isolation when sessionId is provided</description></item>
/// <item><description>Configurable default TTL and maximum TTL limits</description></item>
/// </list>
/// </para>
/// </remarks>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class InMemoryMcpTaskStore : IMcpTaskStore, IDisposable
{
    private readonly ConcurrentDictionary<string, TaskEntry> _tasks = new();
    private readonly TimeSpan? _defaultTtl;
    private readonly TimeSpan? _maxTtl;
    private readonly TimeSpan _pollInterval;
#if MCP_TEST_TIME_PROVIDER
    private readonly ITimer? _cleanupTimer;
#else
    private readonly Timer? _cleanupTimer;
#endif
    private readonly int _pageSize;
    private readonly int? _maxTasks;
    private readonly int? _maxTasksPerSession;
#if MCP_TEST_TIME_PROVIDER
    private readonly TimeProvider _timeProvider;
#endif

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryMcpTaskStore"/> class.
    /// </summary>
    /// <param name="defaultTtl">
    /// Default TTL to use when task creation does not specify a TTL. Null means unlimited.
    /// </param>
    /// <param name="maxTtl">
    /// Maximum TTL allowed. If a task requests a longer TTL, it will be capped to this value.
    /// Null means no maximum limit.
    /// </param>
    /// <param name="pollInterval">
    /// Advertised polling interval for tasks. Default is 1 second.
    /// This value is used when creating new tasks to indicate how frequently clients should poll for updates.
    /// </param>
    /// <param name="cleanupInterval">
    /// Interval for running background cleanup of expired tasks. Default is 1 minute.
    /// Pass <see cref="Timeout.InfiniteTimeSpan"/> to disable automatic cleanup.
    /// </param>
    /// <param name="pageSize">
    /// Maximum number of tasks to return per page in <see cref="ListTasksAsync"/>. Default is 100.
    /// </param>
    /// <param name="maxTasks">
    /// Maximum number of tasks allowed in the store globally. Null means unlimited.
    /// When the limit is reached, <see cref="CreateTaskAsync"/> will throw <see cref="InvalidOperationException"/>.
    /// </param>
    /// <param name="maxTasksPerSession">
    /// Maximum number of tasks allowed per session. Null means unlimited.
    /// When the limit is reached for a session, <see cref="CreateTaskAsync"/> will throw <see cref="InvalidOperationException"/>.
    /// </param>
    public InMemoryMcpTaskStore(
        TimeSpan? defaultTtl = null,
        TimeSpan? maxTtl = null,
        TimeSpan? pollInterval = null,
        TimeSpan? cleanupInterval = null,
        int pageSize = 100,
        int? maxTasks = null,
        int? maxTasksPerSession = null)
    {
        if (defaultTtl.HasValue && maxTtl.HasValue && defaultTtl.Value > maxTtl.Value)
        {
            throw new ArgumentException(
                $"Default TTL ({defaultTtl.Value}) cannot exceed maximum TTL ({maxTtl.Value}).",
                nameof(defaultTtl));
        }

        pollInterval ??= TimeSpan.FromSeconds(1);
        if (pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollInterval),
                pollInterval,
                "Poll interval must be positive.");
        }

        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                pageSize,
                "Page size must be positive.");
        }

        if (maxTasks is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTasks),
                maxTasks,
                "Max tasks must be positive.");
        }

        if (maxTasksPerSession is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTasksPerSession),
                maxTasksPerSession,
                "Max tasks per session must be positive.");
        }

        _defaultTtl = defaultTtl;
        _maxTtl = maxTtl;
        _pollInterval = pollInterval.Value;
        _pageSize = pageSize;
        _maxTasks = maxTasks;
        _maxTasksPerSession = maxTasksPerSession;
#if MCP_TEST_TIME_PROVIDER
        _timeProvider = TimeProvider.System;
#endif

        cleanupInterval ??= TimeSpan.FromMinutes(1);
        if (cleanupInterval.Value != Timeout.InfiniteTimeSpan)
        {
#if MCP_TEST_TIME_PROVIDER
            _cleanupTimer = _timeProvider.CreateTimer(CleanupExpiredTasks, null, cleanupInterval.Value, cleanupInterval.Value);
#else
            _cleanupTimer = new Timer(CleanupExpiredTasks, null, cleanupInterval.Value, cleanupInterval.Value);
#endif
        }
    }

#if MCP_TEST_TIME_PROVIDER
    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryMcpTaskStore"/> class with a custom time provider.
    /// This constructor is only available for testing purposes.
    /// </summary>
    internal InMemoryMcpTaskStore(
        TimeSpan? defaultTtl,
        TimeSpan? maxTtl,
        TimeSpan? pollInterval,
        TimeSpan? cleanupInterval,
        int pageSize,
        int? maxTasks,
        int? maxTasksPerSession,
        TimeProvider timeProvider)
        : this(defaultTtl, maxTtl, pollInterval, cleanupInterval, pageSize, maxTasks, maxTasksPerSession)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }
#endif

    /// <inheritdoc/>
    public Task<McpTask> CreateTaskAsync(
        McpTaskMetadata taskParams,
        RequestId requestId,
        JsonRpcRequest request,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        // Check global task limit
        if (_maxTasks is { } maxTasks && _tasks.Count >= maxTasks)
        {
            throw new InvalidOperationException(
                $"Maximum number of tasks ({maxTasks}) has been reached. Cannot create new task.");
        }

        // Check per-session task limit
        if (_maxTasksPerSession is { } maxPerSession && sessionId is not null)
        {
            var sessionTaskCount = _tasks.Values.Count(e => e.SessionId == sessionId && !IsExpired(e));
            if (sessionTaskCount >= maxPerSession)
            {
                throw new InvalidOperationException(
                    $"Maximum number of tasks per session ({maxPerSession}) has been reached for session '{sessionId}'. Cannot create new task.");
            }
        }

        var taskId = GenerateTaskId();
        var now = GetUtcNow();

        // Determine TTL: use requested, fall back to default, respect max limit
        var ttl = taskParams.TimeToLive ?? _defaultTtl;
        if (ttl is { } ttlValue && _maxTtl is { } maxTtlValue && ttlValue > maxTtlValue)
        {
            ttl = maxTtlValue;
        }

        TaskEntry entry = new()
        {
            TaskId = taskId,
            Status = McpTaskStatus.Working,
            CreatedAt = now,
            LastUpdatedAt = now,
            TimeToLive = ttl,
            PollInterval = _pollInterval,
            RequestId = requestId,
            Request = request,
            SessionId = sessionId
        };

        if (!_tasks.TryAdd(taskId, entry))
        {
            // This should be extremely rare with GUID-based IDs
            throw new InvalidOperationException($"Task ID collision: {taskId}");
        }

        return Task.FromResult(entry.ToMcpTask());
    }

    /// <inheritdoc/>
    public Task<McpTask?> GetTaskAsync(string taskId, string? sessionId = null, CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(taskId, out var entry))
        {
            return Task.FromResult<McpTask?>(null);
        }

        // Enforce session isolation if sessionId is provided
        if (sessionId != null && entry.SessionId != sessionId)
        {
            return Task.FromResult<McpTask?>(null);
        }

        return Task.FromResult<McpTask?>(entry.ToMcpTask());
    }

    /// <inheritdoc/>
    public Task<McpTask> StoreTaskResultAsync(
        string taskId,
        McpTaskStatus status,
        JsonElement result,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        if (status is not (McpTaskStatus.Completed or McpTaskStatus.Failed))
        {
            throw new ArgumentException(
                $"Status must be {nameof(McpTaskStatus.Completed)} or {nameof(McpTaskStatus.Failed)}.",
                nameof(status));
        }

        // Retry loop for optimistic concurrency
        while (true)
        {
            if (!_tasks.TryGetValue(taskId, out var entry))
            {
                throw new InvalidOperationException($"Task not found: {taskId}");
            }

            // Enforce session isolation
            if (sessionId != null && entry.SessionId != sessionId)
            {
                throw new InvalidOperationException($"Task not found: {taskId}");
            }

            // Prevent overwriting terminal state
            if (IsTerminalStatus(entry.Status))
            {
                throw new InvalidOperationException(
                    $"Cannot store result for task in terminal state: {entry.Status}");
            }

            var updatedEntry = new TaskEntry(entry)
            {
                Status = status,
                LastUpdatedAt = GetUtcNow(),
                StoredResult = result
            };

            if (_tasks.TryUpdate(taskId, updatedEntry, entry))
            {
                return Task.FromResult(updatedEntry.ToMcpTask());
            }

            // Entry was modified by another thread, retry
        }
    }

    /// <inheritdoc/>
    public Task<JsonElement> GetTaskResultAsync(string taskId, string? sessionId = null, CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(taskId, out var entry))
        {
            throw new InvalidOperationException($"Task not found: {taskId}");
        }

        // Enforce session isolation
        if (sessionId != entry.SessionId)
        {
            throw new InvalidOperationException($"Invalid sessionId: {sessionId} provided for {taskId}");
        }

        if (entry.StoredResult is not { } storedResult)
        {
            throw new InvalidOperationException($"No result stored for task: {taskId}");
        }

        return Task.FromResult(storedResult);
    }

    /// <inheritdoc/>
    public Task<McpTask> UpdateTaskStatusAsync(
        string taskId,
        McpTaskStatus status,
        string? statusMessage,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        // Retry loop for optimistic concurrency
        while (true)
        {
            if (!_tasks.TryGetValue(taskId, out var entry))
            {
                throw new InvalidOperationException($"Task not found: {taskId}");
            }

            // Enforce session isolation
            if (sessionId != null && entry.SessionId != sessionId)
            {
                throw new InvalidOperationException($"Task not found: {taskId}");
            }

            var updatedEntry = new TaskEntry(entry)
            {
                Status = status,
                StatusMessage = statusMessage,
                LastUpdatedAt = GetUtcNow(),
            };

            if (_tasks.TryUpdate(taskId, updatedEntry, entry))
            {
                return Task.FromResult(updatedEntry.ToMcpTask());
            }

            // Entry was modified by another thread, retry
        }
    }

    /// <inheritdoc/>
    public Task<ListTasksResult> ListTasksAsync(
        string? cursor = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        // Stream enumeration - filter by session, exclude expired, apply keyset pagination
        var query = _tasks.Values
            .Where(e => sessionId == null || e.SessionId == sessionId)
            .Where(e => !IsExpired(e));

        // Apply keyset filter if cursor provided: TaskId > cursor
        // UUID v7 task IDs are monotonically increasing and inherently time-ordered
        if (cursor != null)
        {
            query = query.Where(e => string.CompareOrdinal(e.TaskId, cursor) > 0);
        }

        // Order by TaskId for stable, deterministic pagination
        // UUID v7 task IDs sort chronologically due to embedded timestamp
        var page = query
            .OrderBy(e => e.TaskId, StringComparer.Ordinal)
            .Take(_pageSize + 1) // Take one extra to check if there's a next page
            .Select(e => e.ToMcpTask())
            .ToList();

        // Set nextCursor if we have more results
        string? nextCursor;
        if (page.Count > _pageSize)
        {
            var lastItemInPage = page[_pageSize - 1]; // Last item we'll actually return
            nextCursor = lastItemInPage.TaskId;
            page.RemoveAt(_pageSize); // Remove the extra item
        }
        else
        {
            nextCursor = null;
        }

        return Task.FromResult(new ListTasksResult
        {
            Tasks = page.ToArray(),
            NextCursor = nextCursor
        });
    }

    /// <inheritdoc/>
    public Task<McpTask> CancelTaskAsync(string taskId, string? sessionId = null, CancellationToken cancellationToken = default)
    {
        // Retry loop for optimistic concurrency
        while (true)
        {
            if (!_tasks.TryGetValue(taskId, out var entry))
            {
                throw new InvalidOperationException($"Task not found: {taskId}");
            }

            // Enforce session isolation
            if (sessionId != null && entry.SessionId != sessionId)
            {
                throw new InvalidOperationException($"Task not found: {taskId}");
            }

            // If already in terminal state, return unchanged
            if (IsTerminalStatus(entry.Status))
            {
                return Task.FromResult(entry.ToMcpTask());
            }

            var updatedEntry = new TaskEntry(entry)
            {
                Status = McpTaskStatus.Cancelled,
                LastUpdatedAt = GetUtcNow(),
            };

            if (_tasks.TryUpdate(taskId, updatedEntry, entry))
            {
                return Task.FromResult(updatedEntry.ToMcpTask());
            }

            // Entry was modified by another thread, retry
        }
    }

    /// <summary>
    /// Disposes the task store and stops background cleanup.
    /// </summary>
    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }

    private string GenerateTaskId() =>
        IdHelpers.CreateMonotonicId(GetUtcNow());

    private static bool IsTerminalStatus(McpTaskStatus status) =>
        status is McpTaskStatus.Completed or McpTaskStatus.Failed or McpTaskStatus.Cancelled;

#if MCP_TEST_TIME_PROVIDER
    private DateTimeOffset GetUtcNow() => _timeProvider.GetUtcNow();
#else
    private static DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;
#endif

#if MCP_TEST_TIME_PROVIDER
    private bool IsExpired(TaskEntry entry)
#else
    private static bool IsExpired(TaskEntry entry)
#endif
    {
        if (entry.TimeToLive == null)
        {
            return false; // Unlimited lifetime
        }

        var expirationTime = entry.CreatedAt + entry.TimeToLive.Value;
        return GetUtcNow() >= expirationTime;
    }

    private void CleanupExpiredTasks(object? state)
    {
        var expiredTaskIds = _tasks
            .Where(kvp => IsExpired(kvp.Value))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var taskId in expiredTaskIds)
        {
            _tasks.TryRemove(taskId, out _);
        }
    }

    private sealed class TaskEntry
    {
        // Flattened McpTask properties
        public required string TaskId { get; init; }
        public required McpTaskStatus Status { get; init; }
        public string? StatusMessage { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required DateTimeOffset LastUpdatedAt { get; init; }
        public TimeSpan? TimeToLive { get; init; }
        public TimeSpan? PollInterval { get; init; }

        // Request metadata
        public required RequestId RequestId { get; init; }
        public required JsonRpcRequest Request { get; init; }
        public required string? SessionId { get; init; }
        public JsonElement? StoredResult { get; init; }

        /// <summary>
        /// Copy constructor for creating modified copies.
        /// </summary>
        [SetsRequiredMembers]
        public TaskEntry(TaskEntry source)
        {
            TaskId = source.TaskId;
            Status = source.Status;
            StatusMessage = source.StatusMessage;
            CreatedAt = source.CreatedAt;
            LastUpdatedAt = source.LastUpdatedAt;
            TimeToLive = source.TimeToLive;
            PollInterval = source.PollInterval;
            RequestId = source.RequestId;
            Request = source.Request;
            SessionId = source.SessionId;
            StoredResult = source.StoredResult;
        }

        /// <summary>
        /// Default constructor for initial creation.
        /// </summary>
        public TaskEntry() { }

        /// <summary>
        /// Converts this entry back to an McpTask for external consumption.
        /// </summary>
        public McpTask ToMcpTask() => new()
        {
            TaskId = TaskId,
            Status = Status,
            StatusMessage = StatusMessage,
            CreatedAt = CreatedAt,
            LastUpdatedAt = LastUpdatedAt,
            TimeToLive = TimeToLive,
            PollInterval = PollInterval
        };
    }
}
