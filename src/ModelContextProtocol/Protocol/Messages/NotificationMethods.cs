namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// A class containing constants for notification methods.
/// </summary>
public static class NotificationMethods
{
    /// <summary>
    /// Sent by the server when the list of tools changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This notification informs clients that the set of available tools has been modified.
    /// Changes may include tools being added, removed, or updated. Upon receiving this 
    /// notification, clients should refresh their tool list by calling the appropriate 
    /// method to get the updated list of tools.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// // Server-side code to notify clients about tool list changes
    /// await server.SendNotificationAsync(
    ///     NotificationMethods.ToolListChangedNotification,
    ///     cancellationToken: cancellationToken);
    /// </code>
    /// </para>
    /// </remarks>
    public const string ToolListChangedNotification = "notifications/tools/list_changed";

    /// <summary>
    /// Sent by the server when the list of prompts changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This notification informs clients that the set of available prompts has been modified.
    /// Changes may include prompts being added, removed, or updated. Upon receiving this 
    /// notification, clients should refresh their prompt list by calling the appropriate 
    /// method to get the updated list of prompts.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// // Server-side code to notify clients about prompt list changes
    /// await server.SendNotificationAsync(
    ///     NotificationMethods.PromptListChangedNotification,
    ///     cancellationToken: cancellationToken);
    /// </code>
    /// </para>
    /// </remarks>
    public const string PromptListChangedNotification = "notifications/prompts/list_changed";

    /// <summary>
    /// Sent by the server when the list of resources changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This notification informs clients that the set of available resources has been modified.
    /// Changes may include resources being added, removed, or updated. Upon receiving this 
    /// notification, clients should refresh their resource list by calling the appropriate 
    /// method to get the updated list of resources.
    /// </para>
    /// <para>
    /// This notification differs from <see cref="ResourceUpdatedNotification"/> in that it indicates
    /// a change to the collection of resources itself, rather than an update to a specific resource's content.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// // Server-side code to notify clients about resource list changes
    /// await server.SendNotificationAsync(
    ///     NotificationMethods.ResourceListChangedNotification,
    ///     cancellationToken: cancellationToken);
    /// </code>
    /// </para>
    /// </remarks>
    public const string ResourceListChangedNotification = "notifications/resources/list_changed";

    /// <summary>
    /// Sent by the server when a resource is updated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This notification is used to inform clients about changes to a specific resource they have subscribed to.
    /// The notification includes the URI of the updated resource in its parameters.
    /// </para>
    /// <para>
    /// Clients can subscribe to resource updates by maintaining a collection of resource URIs they are interested in.
    /// When a resource is updated, the server sends this notification to all clients that have subscribed to that resource.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// // Server-side code to send a notification when a resource is updated
    /// await server.SendNotificationAsync(
    ///     NotificationMethods.ResourceUpdatedNotification,
    ///     new ResourceUpdatedNotificationParams
    ///     {
    ///         Uri = "resource://documents/123"
    ///     },
    ///     cancellationToken);
    /// </code>
    /// </para>
    /// </remarks>
    public const string ResourceUpdatedNotification = "notifications/resources/updated";

    /// <summary>
    /// Sent by the client when roots have been updated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This notification informs the server that the client's root directories or base resources
    /// have changed. Roots typically represent top-level directories or container resources that
    /// the client is working with. When roots change, the server may need to update its view of
    /// the client's workspace or recalculate resource availability.
    /// </para>
    /// <para>
    /// After receiving this notification, the server may need to refresh any cached information
    /// about the client's workspace structure.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// // Client-side code to notify the server about root changes
    /// await client.SendNotificationAsync(
    ///     NotificationMethods.RootsUpdatedNotification,
    ///     cancellationToken: cancellationToken);
    /// </code>
    /// </para>
    /// </remarks>
    public const string RootsUpdatedNotification = "notifications/roots/list_changed";

    /// <summary>
    /// Sent by the server when a log message is generated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This notification is used by the server to send log messages to clients. Log messages can include
    /// different severity levels (debug, info, warning, error) and optional logger name to identify
    /// the source component.
    /// </para>
    /// <para>
    /// The minimum logging level that triggers notifications can be controlled by clients using the
    /// <c>logging/setLevel</c> request. If no level has been set by a client, the server may determine
    /// which messages to send based on its own configuration.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// // Server-side code to send a log notification
    /// await server.SendNotificationAsync(
    ///     NotificationMethods.LoggingMessageNotification,
    ///     new LoggingMessageNotificationParams
    ///     {
    ///         Level = LoggingLevel.Info,
    ///         Logger = "model-server",
    ///         Data = JsonSerializer.SerializeToElement("Model loaded successfully")
    ///     });
    /// </code>
    /// </para>
    /// </remarks>
    public const string LoggingMessageNotification = "notifications/message";

    /// <summary>
    /// Sent from the client to the server after initialization has finished.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This notification is sent by the client after it has received and processed the server's response to the 
    /// <see cref="RequestMethods.Initialize"/> request. It signals that the client is ready to begin normal operation 
    /// and that the initialization phase is complete.
    /// </para>
    /// <para>
    /// After receiving this notification, the server can begin sending notifications and processing
    /// further requests from the client. This notification has no parameters and expects no response.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// await client.SendMessageAsync(
    ///     new JsonRpcNotification { 
    ///         Method = NotificationMethods.InitializedNotification 
    ///     },
    ///     cancellationToken
    /// );
    /// </code>
    /// </para>
    /// </remarks>
    public const string InitializedNotification = "notifications/initialized";

    /// <summary>
    /// Sent to inform the receiver of a progress update for a long-running request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This notification provides updates on the progress of long-running operations. It includes
    /// a progress token that associates the notification with a specific request, the current progress value,
    /// and optionally, a total value and a descriptive message.
    /// </para>
    /// <para>
    /// Progress notifications enable clients to display progress indicators for operations that might take
    /// significant time to complete, such as large file uploads, complex computations, or resource-intensive
    /// processing tasks.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// // Server-side code to send a progress notification
    /// await endpoint.SendNotificationAsync(
    ///     NotificationMethods.ProgressNotification,
    ///     new ProgressNotification
    ///     {
    ///         ProgressToken = progressToken,
    ///         Progress = new ProgressNotificationValue 
    ///         { 
    ///             Progress = 75,
    ///             Total = 100,
    ///             Message = "Processing 75 of 100 items" 
    ///         }
    ///     },
    ///     cancellationToken);
    /// </code>
    /// </para>
    /// </remarks>
    public const string ProgressNotification = "notifications/progress";

    /// <summary>
    /// Sent by either side to indicate that it is cancelling a previously-issued request.
    /// </summary>
    /// <remarks>
    /// The request SHOULD still be in-flight, but due to communication latency, it is always possible that this notification
    /// MAY arrive after the request has already finished.
    /// 
    /// This notification indicates that the result will be unused, so any associated processing SHOULD cease.
    /// 
    /// A client MUST NOT attempt to cancel its `initialize` request.".
    /// </remarks>
    public const string CancelledNotification = "notifications/cancelled";
}