using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters used with a <see cref="RequestMethods.SubscriptionsListen"/> request.
/// </summary>
/// <remarks>
/// <para>
/// Introduced by the 2026-07-28 protocol revision (SEP-2575). The client uses this request to open a
/// long-lived channel for receiving notifications outside the context of a specific request.
/// </para>
/// <para>
/// Per-request metadata (protocol version, client info, client capabilities, optional log level)
/// flows through the inherited <see cref="RequestParams.Meta"/> property under the
/// <c>io.modelcontextprotocol/*</c> keys.
/// </para>
/// </remarks>
public sealed class SubscriptionsListenRequestParams : RequestParams
{
    /// <summary>
    /// Gets or sets the notifications the client wants to receive on this subscription stream.
    /// </summary>
    /// <remarks>
    /// Each notification type is opt-in; the server MUST NOT send notification types the client
    /// has not explicitly requested here. The server's
    /// <see cref="NotificationMethods.SubscriptionsAcknowledgedNotification"/> reports the subset
    /// of requested notifications the server actually supports.
    /// </remarks>
    [JsonPropertyName("notifications")]
    public required SubscriptionsListenNotifications Notifications { get; set; }
}

/// <summary>
/// Describes the set of notification types a client wants to receive (or that a server has agreed
/// to deliver) for a <see cref="RequestMethods.SubscriptionsListen"/> subscription.
/// </summary>
public sealed class SubscriptionsListenNotifications
{
    /// <summary>
    /// Gets or sets a value indicating whether to receive
    /// <see cref="NotificationMethods.ToolListChangedNotification"/> notifications.
    /// </summary>
    [JsonPropertyName("toolsListChanged")]
    public bool? ToolsListChanged { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to receive
    /// <see cref="NotificationMethods.PromptListChangedNotification"/> notifications.
    /// </summary>
    [JsonPropertyName("promptsListChanged")]
    public bool? PromptsListChanged { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to receive
    /// <see cref="NotificationMethods.ResourceListChangedNotification"/> notifications.
    /// </summary>
    [JsonPropertyName("resourcesListChanged")]
    public bool? ResourcesListChanged { get; set; }

    /// <summary>
    /// Gets or sets the list of resource URIs to subscribe to for
    /// <see cref="NotificationMethods.ResourceUpdatedNotification"/> notifications.
    /// </summary>
    /// <remarks>
    /// Replaces the legacy <see cref="RequestMethods.ResourcesSubscribe"/> /
    /// <see cref="RequestMethods.ResourcesUnsubscribe"/> RPCs from prior protocol revisions.
    /// </remarks>
    [JsonPropertyName("resourceSubscriptions")]
    public IList<string>? ResourceSubscriptions { get; set; }
}
