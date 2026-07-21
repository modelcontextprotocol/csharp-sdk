namespace ModelContextProtocol.Extensions.Tasks;

/// <summary>
/// Provides constants for the MCP Tasks extension (SEP-2663).
/// </summary>
public static class TasksProtocol
{
    /// <summary>
    /// The extension identifier for the MCP Tasks extension.
    /// </summary>
    public const string ExtensionId = "io.modelcontextprotocol/tasks";

    /// <summary>
    /// The name of the request method sent from the client to poll for task completion.
    /// </summary>
    public const string MethodTasksGet = "tasks/get";

    /// <summary>
    /// The name of the request method sent from the client to provide input responses to a task.
    /// </summary>
    public const string MethodTasksUpdate = "tasks/update";

    /// <summary>
    /// The name of the request method sent from the client to signal intent to cancel a task.
    /// </summary>
    public const string MethodTasksCancel = "tasks/cancel";

    /// <summary>
    /// The name of the notification sent by the server when a task's status changes.
    /// </summary>
    public const string NotificationTaskStatus = "notifications/tasks/status";

    /// <summary>
    /// The metadata key used to associate requests, responses, and notifications with a task.
    /// </summary>
    public const string MetaRelatedTask = "io.modelcontextprotocol/related-task";
}
