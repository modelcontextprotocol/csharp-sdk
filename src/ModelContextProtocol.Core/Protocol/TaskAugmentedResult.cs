namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the result of a request that supports task-augmented execution, which may be either
/// the standard result or a <see cref="CreateTaskResult"/> indicating asynchronous processing.
/// </summary>
/// <typeparam name="TResult">The standard result type for the request (e.g., <see cref="CallToolResult"/>).</typeparam>
/// <remarks>
/// <para>
/// When a server supports the <c>io.modelcontextprotocol/tasks</c> extension and the client declares
/// the extension capability on its request, the server may return a <see cref="CreateTaskResult"/>
/// instead of the standard result. This type represents that polymorphic response.
/// </para>
/// <para>
/// Use <see cref="IsTask"/> to determine which variant was returned, then access either
/// <see cref="Result"/> for the immediate result or <see cref="TaskCreated"/> for the task handle.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md">SEP-2663</see>
/// specification for details.
/// </para>
/// </remarks>
public class TaskAugmentedResult<TResult> where TResult : Result
{
    private readonly TResult? _result;
    private readonly CreateTaskResult? _taskCreated;

    /// <summary>
    /// Initializes a new instance of <see cref="TaskAugmentedResult{TResult}"/> with an immediate result.
    /// </summary>
    /// <param name="result">The standard result returned by the server.</param>
    public TaskAugmentedResult(TResult result)
    {
        Throw.IfNull(result);
        _result = result;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="TaskAugmentedResult{TResult}"/> with a task handle.
    /// </summary>
    /// <param name="taskCreated">The task creation result returned by the server.</param>
    public TaskAugmentedResult(CreateTaskResult taskCreated)
    {
        Throw.IfNull(taskCreated);
        _taskCreated = taskCreated;
    }

    /// <summary>
    /// Gets a value indicating whether the server created a task instead of returning an immediate result.
    /// </summary>
    public bool IsTask => _taskCreated is not null;

    /// <summary>
    /// Gets the immediate result, or <see langword="null"/> if the server created a task.
    /// </summary>
    public TResult? Result => _result;

    /// <summary>
    /// Gets the task creation result, or <see langword="null"/> if the server returned an immediate result.
    /// </summary>
    public CreateTaskResult? TaskCreated => _taskCreated;
}
