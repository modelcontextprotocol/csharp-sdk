namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// A class containing constants for notification methods.
/// </summary>
public static class NotificationMethods
{
    /// <summary>
    /// Sent by the server when the list of tools changes.
    /// </summary>
    public const string ToolListChangedNotification = "notifications/tools/list_changed";

    /// <summary>
    /// Sent by the server when the list of prompts changes.
    /// </summary>
    public const string PromptListChangedNotification = "notifications/prompts/list_changed";

    /// <summary>
    /// Sent by the server when the list of resources changes.
    /// </summary>
    public const string ResourceListChangedNotification = "notifications/resources/list_changed";

    /// <summary>
    /// Sent by the server when a resource is updated.
    /// </summary>
    public const string ResourceUpdatedNotification = "notifications/resources/updated";

    /// <summary>
    /// Sent by the client when roots have been updated.
    /// </summary>
    public const string RootsUpdatedNotification = "notifications/roots/list_changed";

    /// <summary>
    /// Sent by the server when a log message is generated.
    /// </summary>
    public const string LoggingMessageNotification = "notifications/message";

    /// <summary>
    /// Sent from the client to the server after initialization has finished.
    /// </summary>
    public const string InitializedNotification = "notifications/initialized";

    /// <summary>
    /// Sent to inform the receiver of a progress update for a long-running request.
    /// </summary>
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