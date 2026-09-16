using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters sent with a <see cref="NotificationMethods.SubscriptionsAcknowledgedNotification"/>.
/// </summary>
/// <remarks>
/// <para>
/// Introduced by the 2026-07-28 protocol revision (SEP-2575). This notification is the first message on a
/// <see cref="RequestMethods.SubscriptionsListen"/> response stream and informs the client which
/// subset of requested notification types the server has agreed to deliver.
/// </para>
/// </remarks>
public sealed class SubscriptionsAcknowledgedNotificationParams
{
    /// <summary>
    /// Gets or sets the notification subscriptions the server has agreed to honor.
    /// </summary>
    /// <remarks>
    /// Only includes notification types the server actually supports. If the client requested an
    /// unsupported notification type (e.g., <c>promptsListChanged</c> when the server has no prompts),
    /// it is omitted from this set.
    /// </remarks>
    [JsonPropertyName("notifications")]
    public required SubscriptionsListenNotifications Notifications { get; set; }
}
