using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ModelContextProtocol;

/// <summary>
/// Provides an interface for pluggable task storage implementations in MCP servers.
/// </summary>
/// <remarks>
/// <para>
/// The task store is responsible for managing the lifecycle of tasks, including creation,
/// status updates, result storage, and retrieval. Implementations must be thread-safe and
/// may support session-based isolation for multi-session scenarios.
/// </para>
/// <para>
/// TTL (Time To Live) Management: Implementations may override the requested TTL value in
/// <see cref="McpTaskMetadata.TimeToLive"/> to enforce resource limits. The actual TTL
/// used is returned in the <see cref="McpTask.TimeToLive"/> property. A null TTL indicates
/// unlimited lifetime. Tasks may be deleted after their TTL expires, regardless of status.
/// </para>
/// </remarks>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public interface IMcpTaskStore
{
    /// <summary>
    /// Creates a new task for tracking an asynchronous operation.
    /// </summary>
    /// <param name="taskParams">Metadata for the task, including requested TTL.</param>
    /// <param name="requestId">The JSON-RPC request ID that initiated this task.</param>
    /// <param name="request">The original JSON-RPC request that triggered task creation.</param>
    /// <param name="sessionId">Optional session identifier for multi-session isolation.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// A new <see cref="McpTask"/> with a unique task ID, initial status of <see cref="McpTaskStatus.Working"/>,
    /// and the actual TTL that will be used (which may differ from the requested TTL).
    /// </returns>
    /// <remarks>
    /// Implementations must generate a unique task ID and set the <see cref="McpTask.CreatedAt"/>
    /// and <see cref="McpTask.LastUpdatedAt"/> timestamps. The implementation may override the
    /// requested TTL to enforce storage limits.
    /// </remarks>
    Task<McpTask> CreateTaskAsync(
        McpTaskMetadata taskParams,
        RequestId requestId,
        JsonRpcRequest request,
        string? sessionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a task by its unique identifier.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task to retrieve.</param>
    /// <param name="sessionId">Optional session identifier for access control.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// The <see cref="McpTask"/> if found and accessible, otherwise <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// Returns null if the task does not exist or if session-based access control denies access.
    /// </remarks>
    Task<McpTask?> GetTaskAsync(string taskId, string? sessionId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the final result of a task that has reached a terminal status.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task.</param>
    /// <param name="status">The terminal status: <see cref="McpTaskStatus.Completed"/> or <see cref="McpTaskStatus.Failed"/>.</param>
    /// <param name="result">The operation result to store as a JSON element.</param>
    /// <param name="sessionId">Optional session identifier for access control.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The updated <see cref="McpTask"/> with the new status and result stored.</returns>
    /// <remarks>
    /// <para>
    /// The <paramref name="status"/> must be either <see cref="McpTaskStatus.Completed"/> or
    /// <see cref="McpTaskStatus.Failed"/>. This method updates the task status and stores
    /// the result for later retrieval via <see cref="GetTaskResultAsync"/>.
    /// </para>
    /// <para>
    /// Implementations should throw <see cref="InvalidOperationException"/> if called on a task
    /// that is already in a terminal state, to prevent result overwrites.
    /// </para>
    /// </remarks>
    Task<McpTask> StoreTaskResultAsync(
        string taskId,
        McpTaskStatus status,
        JsonElement result,
        string? sessionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the stored result of a completed or failed task.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task.</param>
    /// <param name="sessionId">Optional session identifier for access control.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The stored operation result as a JSON element.</returns>
    /// <remarks>
    /// This method should only be called on tasks in terminal states (<see cref="McpTaskStatus.Completed"/>
    /// or <see cref="McpTaskStatus.Failed"/>). The result contains the JSON representation of the
    /// original operation result (e.g., <see cref="CallToolResult"/> for tools/call).
    /// </remarks>
    Task<JsonElement> GetTaskResultAsync(string taskId, string? sessionId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the status and optional status message of a task.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task.</param>
    /// <param name="status">The new status to set.</param>
    /// <param name="statusMessage">Optional diagnostic message describing the status change.</param>
    /// <param name="sessionId">Optional session identifier for access control.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The updated <see cref="McpTask"/> with the new status applied.</returns>
    /// <remarks>
    /// This method updates the task's <see cref="McpTask.Status"/>, <see cref="McpTask.StatusMessage"/>,
    /// and <see cref="McpTask.LastUpdatedAt"/> properties. Common uses include transitioning to
    /// <see cref="McpTaskStatus.Cancelled"/>, <see cref="McpTaskStatus.InputRequired"/>, or updating
    /// progress messages while in <see cref="McpTaskStatus.Working"/> status.
    /// </remarks>
    Task<McpTask> UpdateTaskStatusAsync(
        string taskId,
        McpTaskStatus status,
        string? statusMessage,
        string? sessionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists tasks with pagination support.
    /// </summary>
    /// <param name="cursor">Optional cursor for pagination, from a previous call's nextCursor value.</param>
    /// <param name="sessionId">Optional session identifier for filtering tasks by session.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A <see cref="ListTasksResult"/> containing the tasks and an optional cursor for the next page.</returns>
    /// <remarks>
    /// When <paramref name="sessionId"/> is provided, implementations should filter to only return
    /// tasks associated with that session. The cursor format is implementation-specific.
    /// </remarks>
    Task<ListTasksResult> ListTasksAsync(
        string? cursor = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to cancel a task, transitioning it to <see cref="McpTaskStatus.Cancelled"/> status.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task to cancel.</param>
    /// <param name="sessionId">Optional session identifier for access control.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// The updated <see cref="McpTask"/>. If the task is already in a terminal state
    /// (<see cref="McpTaskStatus.Completed"/>, <see cref="McpTaskStatus.Failed"/>, or
    /// <see cref="McpTaskStatus.Cancelled"/>), the task is returned unchanged.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method must be idempotent. If called on a task that is already in a terminal state,
    /// it returns the current task without error. This behavior differs from the MCP specification
    /// but ensures idempotency and avoids race conditions between cancellation and task completion.
    /// </para>
    /// <para>
    /// For tasks not in a terminal state, the implementation should attempt to stop the underlying
    /// operation and transition the task to <see cref="McpTaskStatus.Cancelled"/> status before returning.
    /// </para>
    /// </remarks>
    Task<McpTask> CancelTaskAsync(string taskId, string? sessionId = null, CancellationToken cancellationToken = default);
}
