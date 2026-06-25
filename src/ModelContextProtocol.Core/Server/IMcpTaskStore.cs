using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides an interface for storing and managing the lifecycle of MCP tasks.
/// </summary>
/// <remarks>
/// <para>
/// The task store manages the state of tasks created by the server's request handling pipeline.
/// When a client signals support for the <c>io.modelcontextprotocol/tasks</c> extension on a request,
/// the server creates a task in the store, executes the work in the background, and stores the result
/// upon completion.
/// </para>
/// <para>
/// Implementations must be thread-safe. The store also provides the backing implementation for
/// <c>tasks/get</c>, <c>tasks/update</c>, and <c>tasks/cancel</c> protocol methods.
/// </para>
/// <para>
/// <b>Lifetime under stateless HTTP:</b> when the server is configured for stateless HTTP
/// (each request creates a fresh server instance), the same <see cref="IMcpTaskStore"/> instance
/// MUST be shared across requests — either by registering the store as a singleton in the DI
/// container, or by backing it with external storage (database, distributed cache, etc.) that
/// every server instance can reach. Otherwise <c>tasks/get</c> polls issued on subsequent
/// requests will see an empty in-memory store and never find the task they are polling for.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md">SEP-2663</see>
/// specification for details on the tasks extension.
/// </para>
/// </remarks>
public interface IMcpTaskStore
{
    /// <summary>
    /// Creates a new task for tracking an asynchronous execution.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// A <see cref="McpTaskInfo"/> with a unique task ID, initial status of <see cref="McpTaskStatus.Working"/>,
    /// and timing metadata (TTL, poll interval).
    /// </returns>
    /// <remarks>
    /// <para>
    /// Implementations must generate a unique task ID and set appropriate timestamps.
    /// The server infrastructure maps the returned <see cref="McpTaskInfo"/> to the appropriate
    /// protocol response type when communicating with clients.
    /// </para>
    /// <para>
    /// Per the MCP specification (SEP-2663 §306), the returned task MUST be durably created
    /// before this method completes: a subsequent <see cref="GetTaskAsync"/> with the returned
    /// <see cref="McpTaskInfo.TaskId"/> MUST resolve, even if it runs on a different process or
    /// node. Implementations backed by eventually-consistent storage must therefore wait for the
    /// write to be visible (e.g., quorum acknowledgement, write-through, or an equivalent
    /// barrier) before returning.
    /// </para>
    /// </remarks>
    Task<McpTaskInfo> CreateTaskAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the current state of a task.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// A <see cref="McpTaskInfo"/> representing the current task state,
    /// or <see langword="null"/> if the task does not exist.
    /// </returns>
    Task<McpTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the result of a completed execution, transitioning the task to <see cref="McpTaskStatus.Completed"/>.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task.</param>
    /// <param name="result">The serialized result payload.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetCompletedAsync(string taskId, JsonElement result, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a task as failed, transitioning it to <see cref="McpTaskStatus.Failed"/>.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task.</param>
    /// <param name="error">The serialized error information.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetFailedAsync(string taskId, JsonElement error, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions the task to <see cref="McpTaskStatus.Cancelled"/>.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task to cancel.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// <see langword="true"/> if the task was successfully cancelled;
    /// <see langword="false"/> if the task does not exist or was already in a terminal state.
    /// </returns>
    Task<bool> SetCancelledAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes input requests that have been satisfied by the provided responses and
    /// raises <see cref="InputResponseReceived"/> for each resolved entry.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task.</param>
    /// <param name="inputResponses">
    /// The input responses keyed by the original request identifier.
    /// Matched input requests are removed from the task's pending set.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <remarks>
    /// <para>
    /// After removing the satisfied requests, if no pending input requests remain the task
    /// transitions back to <see cref="McpTaskStatus.Working"/>. Otherwise it remains in
    /// <see cref="McpTaskStatus.InputRequired"/>.
    /// </para>
    /// <para>
    /// Implementations must raise <see cref="InputResponseReceived"/> for each entry in
    /// <paramref name="inputResponses"/> after updating the store state. In distributed
    /// deployments, this event enables the originating server to be notified even if a
    /// different server instance processes the <c>tasks/update</c> request.
    /// </para>
    /// </remarks>
    Task ResolveInputRequestsAsync(
        string taskId,
        IDictionary<string, InputResponse> inputResponses,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Occurs when an input response is resolved for a task.
    /// </summary>
    /// <remarks>
    /// Implementations must raise this event for each input response resolved in
    /// <see cref="ResolveInputRequestsAsync"/>. Subscribers use this to complete
    /// pending input request waiters (e.g., elicitation or sampling calls that are
    /// awaiting a client response).
    /// </remarks>
    event Action<InputResponseReceivedEventArgs>? InputResponseReceived;

    /// <summary>
    /// Adds input requests to a task, transitioning it to <see cref="McpTaskStatus.InputRequired"/>.
    /// </summary>
    /// <param name="taskId">The unique identifier of the task.</param>
    /// <param name="inputRequests">
    /// The input requests to add. Keys are arbitrary identifiers for matching requests to responses.
    /// Each value is an <see cref="InputRequest"/> wrapping the server-to-client request payload.
    /// New requests are merged with any existing pending requests.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetInputRequestsAsync(
        string taskId,
        IDictionary<string, InputRequest> inputRequests,
        CancellationToken cancellationToken = default);
}
