namespace ModelContextProtocol.Protocol;

/// <summary>
/// Provides constants with the names of common notification methods used in the MCP protocol.
/// </summary>
public static class NotificationMethods
{
    /// <summary>
    /// The name of the notification sent by a server when the list of available tools changes.
    /// </summary>
    /// <remarks>
    /// This notification informs clients that the set of available tools has been modified.
    /// Changes may include tools being added, removed, or updated. Upon receiving this
    /// notification, clients may refresh their tool list by calling the appropriate
    /// method to get the updated list of tools.
    /// </remarks>
    public const string ToolListChangedNotification = "notifications/tools/list_changed";

    /// <summary>
    /// The name of the notification sent by the server when the list of available prompts changes.
    /// </summary>
    /// <remarks>
    /// This notification informs clients that the set of available prompts has been modified.
    /// Changes may include prompts being added, removed, or updated. Upon receiving this
    /// notification, clients may refresh their prompt list by calling the appropriate
    /// method to get the updated list of prompts.
    /// </remarks>
    public const string PromptListChangedNotification = "notifications/prompts/list_changed";

    /// <summary>
    /// The name of the notification sent by the server when the list of available resources changes.
    /// </summary>
    /// <remarks>
    /// This notification informs clients that the set of available resources has been modified.
    /// Changes may include resources being added, removed, or updated. Upon receiving this
    /// notification, clients may refresh their resource list by calling the appropriate
    /// method to get the updated list of resources.
    /// </remarks>
    public const string ResourceListChangedNotification = "notifications/resources/list_changed";

    /// <summary>
    /// The name of the notification sent by the server when a resource is updated.
    /// </summary>
    /// <remarks>
    /// This notification is used to inform clients about changes to a specific resource they have subscribed to.
    /// When a resource is updated, the server sends this notification to all clients that have subscribed to that resource.
    /// </remarks>
    public const string ResourceUpdatedNotification = "notifications/resources/updated";

    /// <summary>
    /// The name of the notification sent by the client when roots have been updated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This notification informs the server that the client's "roots" have changed.
    /// Roots define the boundaries of where servers can operate within the filesystem,
    /// allowing them to understand which directories and files they have access to. Servers
    /// can request the list of roots from supporting clients and receive notifications when that list changes.
    /// </para>
    /// <para>
    /// After receiving this notification, servers can refresh their knowledge of roots by calling the appropriate
    /// method to get the updated list of roots from the client.
    /// </para>
    /// </remarks>
    public const string RootsListChangedNotification = "notifications/roots/list_changed";

    /// <summary>
    /// The name of the notification sent by the server when a log message is generated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This notification is used by the server to send log messages to clients. Log messages can include
    /// different severity levels, such as debug, info, warning, or error, and an optional logger name to
    /// identify the source component.
    /// </para>
    /// <para>
    /// The minimum logging level that triggers notifications can be controlled by clients using the
    /// <see cref="RequestMethods.LoggingSetLevel"/> request. If no level has been set by a client,
    /// the server can determine which messages to send based on its own configuration.
    /// </para>
    /// </remarks>
    public const string LoggingMessageNotification = "notifications/message";

    /// <summary>
    /// The name of the notification sent by the server when a URL-mode elicitation flow completes.
    /// </summary>
    /// <remarks>
    /// This notification references the original elicitation by ID, allowing clients to retry blocked requests
    /// or update their UI state once the out-of-band interaction finishes.
    /// </remarks>
    public const string ElicitationCompleteNotification = "notifications/elicitation/complete";

    /// <summary>
    /// The name of the notification sent from the client to the server after initialization has finished.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This notification is sent by the client after it has received and processed the server's response to the
    /// <see cref="RequestMethods.Initialize"/> request. It signals that the client is ready to begin normal operation
    /// and that the initialization phase is complete.
    /// </para>
    /// <para>
    /// After receiving this notification, the server can begin sending notifications and processing
    /// further requests from the client.
    /// </para>
    /// </remarks>
    public const string InitializedNotification = "notifications/initialized";

    /// <summary>
    /// The name of the notification sent to inform the receiver of a progress update for a long-running request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This notification provides updates on the progress of long-running operations. It includes
    /// a progress token that associates the notification with a specific request, the current progress value,
    /// and optionally, a total value and a descriptive message.
    /// </para>
    /// <para>
    /// Progress notifications can be sent by either the client or the server, depending on the context.
    /// Progress notifications enable clients to display progress indicators for operations that might take
    /// significant time to complete, such as large file uploads, complex computations, or resource-intensive
    /// processing tasks.
    /// </para>
    /// </remarks>
    public const string ProgressNotification = "notifications/progress";

    /// <summary>
    /// The name of the notification sent to indicate that a previously issued request should be canceled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// From the issuer's perspective, the request should still be in-flight. However, due to communication latency,
    /// it is always possible that this notification might arrive after the request has already finished.
    /// </para>
    /// <para>
    /// This notification indicates that the result will be unused, so any associated processing SHOULD cease.
    /// </para>
    /// <para>
    /// A client must not attempt to cancel its `initialize` request.
    /// </para>
    /// </remarks>
    public const string CancelledNotification = "notifications/cancelled";

    /// <summary>
    /// The name of the notification sent when a task status changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When a task status changes, receivers may send this notification to inform the requestor
    /// of the change. This notification includes the full task state.
    /// </para>
    /// <para>
    /// Requestors must not rely on receiving this notification, as it is optional. Receivers
    /// are not required to send status notifications and may choose to only send them for
    /// certain status transitions. Requestors should continue to poll via tasks/get to ensure
    /// they receive status updates.
    /// </para>
    /// </remarks>
    public const string TaskStatusNotification = "notifications/tasks/status";

    /// <summary>
    /// The metadata key used to associate requests, responses, and notifications with a task.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This constant defines the key <c>"io.modelcontextprotocol/related-task"</c> used in the
    /// <c>_meta</c> field to associate messages with their originating task across the entire
    /// request lifecycle.
    /// </para>
    /// <para>
    /// For example, an elicitation that a task-augmented tool call depends on must share the
    /// same related task ID with that tool call's task.
    /// </para>
    /// <para>
    /// For <c>tasks/get</c>, <c>tasks/list</c>, and <c>tasks/cancel</c> operations, this
    /// metadata should not be included as the taskId is already present in the message structure.
    /// </para>
    /// </remarks>
    public const string RelatedTaskMetaKey = "io.modelcontextprotocol/related-task";
}
