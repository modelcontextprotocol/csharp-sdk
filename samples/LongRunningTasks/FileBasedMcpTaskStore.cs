using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LongRunningTasks;

/// <summary>
/// A minimal file-based implementation of <see cref="IMcpTaskStore"/> that demonstrates
/// durable, fault-tolerant task storage using simple time-based completion.
/// </summary>
/// <remarks>
/// <para>
/// This implementation stores task data to disk: task ID, creation timestamp, execution duration,
/// session ID, TTL, and optional result. Task completion is determined by:
/// <list type="bullet">
/// <item><description>Explicit completion or failure via <see cref="StoreTaskResultAsync"/></description></item>
/// <item><description>Explicit cancellation via <see cref="CancelTaskAsync"/></description></item>
/// <item><description>Time-based auto-completion when execution time has elapsed</description></item>
/// </list>
/// </para>
/// <para>
/// The file-based approach enables durability across process restarts - if the server
/// crashes and restarts, tasks can still be queried and will complete based on elapsed time.
/// </para>
/// </remarks>
public sealed partial class FileBasedMcpTaskStore : IMcpTaskStore
{
    private readonly string _storePath;
    private readonly TimeSpan _executionTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileBasedMcpTaskStore"/> class.
    /// </summary>
    /// <param name="storePath">The directory path where task files will be stored.</param>
    /// <param name="executionTime">
    /// The fixed execution time for all tasks. Tasks are reported as completed once this
    /// duration has elapsed since creation. Defaults to 5 seconds.
    /// </param>
    public FileBasedMcpTaskStore(string storePath, TimeSpan? executionTime = null)
    {
        _storePath = storePath ?? throw new ArgumentNullException(nameof(storePath));
        _executionTime = executionTime ?? TimeSpan.FromSeconds(5);
        Directory.CreateDirectory(_storePath);
    }

    /// <inheritdoc/>
    public async Task<McpTask> CreateTaskAsync(
        McpTaskMetadata taskParams,
        RequestId requestId,
        JsonRpcRequest request,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var taskId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        var entry = new TaskFileEntry
        {
            TaskId = taskId,
            SessionId = sessionId,
            Status = McpTaskStatus.Working,
            CreatedAt = now,
            ExecutionTime = _executionTime,
            TimeToLive = taskParams.TimeToLive,
            Result = JsonSerializer.SerializeToElement(request.Params, JsonContext.Default.JsonNode)
        };

        await WriteTaskEntryAsync(GetTaskFilePath(taskId), entry);

        return ToMcpTask(entry);
    }

    /// <inheritdoc/>
    public async Task<McpTask?> GetTaskAsync(
        string taskId,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var entry = await ReadTaskEntryAsync(taskId);
        if (entry is null)
        {
            return null;
        }

        // Session isolation
        if (sessionId is not null && entry.SessionId != sessionId)
        {
            return null;
        }

        // Skip if TTL has expired
        if (IsExpired(entry))
        {
            return null;
        }

        return ToMcpTask(entry);
    }

    /// <inheritdoc/>
    public async Task<McpTask> StoreTaskResultAsync(
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

        var updatedEntry = await UpdateTaskEntryAsync(taskId, sessionId, entry =>
        {
            var effectiveStatus = GetEffectiveStatus(entry);
            if (IsTerminalStatus(effectiveStatus))
            {
                throw new InvalidOperationException(
                    $"Cannot store result for task in terminal state: {effectiveStatus}");
            }

            return entry with
            {
                Status = status,
                Result = result
            };
        });

        return ToMcpTask(updatedEntry);
    }

    /// <inheritdoc/>
    public async Task<JsonElement> GetTaskResultAsync(
        string taskId,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var entry = await ReadTaskEntryAsync(taskId)
            ?? throw new InvalidOperationException($"Task not found: {taskId}");

        if (sessionId is not null && entry.SessionId != sessionId)
        {
            throw new InvalidOperationException($"Task not found: {taskId}");
        }

        var effectiveStatus = GetEffectiveStatus(entry);
        if (!IsTerminalStatus(effectiveStatus))
        {
            throw new InvalidOperationException($"Task not yet completed: {taskId}");
        }

        // Return stored result
        return entry.Result ?? default;
    }

    /// <inheritdoc/>
    public async Task<McpTask> UpdateTaskStatusAsync(
        string taskId,
        McpTaskStatus status,
        string? statusMessage,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var updatedEntry = await UpdateTaskEntryAsync(taskId, sessionId, entry =>
            entry with
            {
                Status = status,
                StatusMessage = statusMessage
            });

        return ToMcpTask(updatedEntry);
    }

    /// <inheritdoc/>
    public async Task<ListTasksResult> ListTasksAsync(
        string? cursor = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var tasks = new List<McpTask>();

        foreach (var file in Directory.EnumerateFiles(_storePath, "*.json"))
        {
            try
            {
                var entry = await ReadTaskEntryFromFileAsync(file);
                if (entry is not null)
                {
                    // Session isolation
                    if (sessionId is not null && entry.SessionId != sessionId)
                    {
                        continue;
                    }

                    // Skip expired tasks
                    if (IsExpired(entry))
                    {
                        continue;
                    }

                    tasks.Add(ToMcpTask(entry));
                }
            }
            catch
            {
                // Skip corrupted or inaccessible files
            }
        }

        tasks.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));

        return new ListTasksResult { Tasks = [.. tasks] };
    }

    /// <inheritdoc/>
    public async Task<McpTask> CancelTaskAsync(
        string taskId,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var updatedEntry = await UpdateTaskEntryAsync(taskId, sessionId, entry =>
        {
            var effectiveStatus = GetEffectiveStatus(entry);
            if (IsTerminalStatus(effectiveStatus))
            {
                // Already terminal, return unchanged
                return entry;
            }

            return entry with { Status = McpTaskStatus.Cancelled };
        });

        return ToMcpTask(updatedEntry);
    }

    private string GetTaskFilePath(string taskId) => Path.Combine(_storePath, $"{taskId}.json");

    /// <summary>
    /// Reads, transforms, and writes a task entry while holding an exclusive file lock.
    /// </summary>
    /// <param name="taskId">The task ID to update.</param>
    /// <param name="sessionId">Optional session ID for access control.</param>
    /// <param name="updateFunc">A function that transforms the entry. May throw to abort the update.</param>
    /// <returns>The updated task entry.</returns>
    private async Task<TaskFileEntry> UpdateTaskEntryAsync(
        string taskId,
        string? sessionId,
        Func<TaskFileEntry, TaskFileEntry> updateFunc)
    {
        var filePath = GetTaskFilePath(taskId);

        // Acquire exclusive lock on the file for the entire read-modify-write cycle
        using var stream = await AcquireFileStreamAsync(filePath, FileMode.Open, FileAccess.ReadWrite);

        var entry = await JsonSerializer.DeserializeAsync(stream, JsonContext.Default.TaskFileEntry)
            ?? throw new InvalidOperationException($"Task not found: {taskId}");

        // Enforce session isolation
        if (sessionId is not null && entry.SessionId != sessionId)
        {
            throw new InvalidOperationException($"Task not found: {taskId}");
        }

        // Apply the transformation (may throw to abort)
        var updatedEntry = updateFunc(entry);

        // Write back to the same stream
        stream.SetLength(0);
        stream.Position = 0;
        await JsonSerializer.SerializeAsync(stream, updatedEntry, JsonContext.Default.TaskFileEntry);

        return updatedEntry;
    }

    private async Task<TaskFileEntry?> ReadTaskEntryAsync(string taskId)
    {
        var filePath = GetTaskFilePath(taskId);
        return File.Exists(filePath) ? await ReadTaskEntryFromFileAsync(filePath) : null;
    }

    private static async Task<TaskFileEntry?> ReadTaskEntryFromFileAsync(string filePath)
    {
        try
        {
            using var stream = await AcquireFileStreamAsync(filePath, FileMode.Open, FileAccess.Read);
            return await JsonSerializer.DeserializeAsync(stream, JsonContext.Default.TaskFileEntry);
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteTaskEntryAsync(string filePath, TaskFileEntry entry)
    {
        using var stream = await AcquireFileStreamAsync(filePath, FileMode.Create, FileAccess.Write);
        await JsonSerializer.SerializeAsync(stream, entry, JsonContext.Default.TaskFileEntry);
    }

    private static async Task<FileStream> AcquireFileStreamAsync(string filePath, FileMode fileMode, FileAccess fileAccess)
    {
        const int MaxRetries = 10;
        const int RetryDelayMs = 50;

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(filePath, fileMode, fileAccess, FileShare.None);
            }
            catch (IOException) when (attempt < MaxRetries)
            {
                await Task.Delay(RetryDelayMs); // File is locked by another process, wait and retry
            }
        }
    }

    private McpTask ToMcpTask(TaskFileEntry entry)
    {
        var now = DateTimeOffset.UtcNow;
        return new McpTask
        {
            TaskId = entry.TaskId,
            Status = GetEffectiveStatus(entry),
            StatusMessage = entry.StatusMessage,
            CreatedAt = entry.CreatedAt,
            LastUpdatedAt = now,
            TimeToLive = entry.TimeToLive
        };
    }

    private static McpTaskStatus GetEffectiveStatus(TaskFileEntry entry)
    {
        // If already in a terminal state, return it
        if (IsTerminalStatus(entry.Status))
        {
            return entry.Status;
        }

        // Check if execution time has elapsed - auto-complete
        if (DateTimeOffset.UtcNow - entry.CreatedAt >= entry.ExecutionTime)
        {
            return McpTaskStatus.Completed;
        }

        return entry.Status;
    }

    private static bool IsTerminalStatus(McpTaskStatus status) =>
        status is McpTaskStatus.Completed or McpTaskStatus.Failed or McpTaskStatus.Cancelled;

    private static bool IsExpired(TaskFileEntry entry) =>
        entry.TimeToLive.HasValue && DateTimeOffset.UtcNow - entry.CreatedAt > entry.TimeToLive.Value;

    /// <summary>
    /// Represents the data stored for each task.
    /// </summary>
    private sealed record TaskFileEntry
    {
        /// <summary>The unique task identifier.</summary>
        public required string TaskId { get; init; }

        /// <summary>The session that created this task.</summary>
        public string? SessionId { get; init; }

        /// <summary>The current task status.</summary>
        public required McpTaskStatus Status { get; init; }

        /// <summary>Optional status message describing the current state.</summary>
        public string? StatusMessage { get; init; }

        /// <summary>When the task was created.</summary>
        public required DateTimeOffset CreatedAt { get; init; }

        /// <summary>How long until the task is considered complete (if not explicitly completed).</summary>
        public required TimeSpan ExecutionTime { get; init; }

        /// <summary>Time to live - task is filtered out after this duration from creation.</summary>
        public TimeSpan? TimeToLive { get; init; }

        /// <summary>The task result - initialized with request params, updated via StoreTaskResultAsync.</summary>
        public JsonElement? Result { get; init; }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(TaskFileEntry))]
    [JsonSerializable(typeof(JsonNode))]
    private sealed partial class JsonContext : JsonSerializerContext;
}
