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
    public const string PromptsListChanged = "notifications/prompts/list_changed";

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
}