namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Sent from the server as the payload of "notifications/resources/updated" notifications whenever a subscribed resource changes.
/// </summary>
/// <remarks>
/// <para>
/// When a client subscribes to resource updates using <see cref="SubscribeRequestParams"/>, the server will
/// send notifications with this payload whenever the subscribed resource is modified. These notifications
/// allow clients to maintain synchronized state without needing to poll the server for changes.
/// </para>
/// <para>
/// The notification only contains the URI of the changed resource. Clients typically need to 
/// make a separate call to <c>ReadResourceAsync</c> to get the updated content if needed.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Server-side: Send resource updated notification
/// var notificationParams = new ResourceUpdatedNotificationParams
/// {
///     Uri = "resource://documents/123"
/// };
/// 
/// await server.SendNotificationAsync(
///     NotificationMethods.ResourceUpdatedNotification,
///     notificationParams,
///     cancellationToken);
///     
/// // Client-side: Register to handle resource update notifications
/// client.RegisterNotificationHandler&lt;ResourceUpdatedNotificationParams&gt;(
///     NotificationMethods.ResourceUpdatedNotification, 
///     (notification, cancellationToken) =>
///     {
///         Console.WriteLine($"Resource updated: {notification.Params?.Uri}");
///         // Fetch the updated resource if needed
///         return Task.CompletedTask;
///     });
/// </code>
/// </example>
/// <seealso cref="SubscribeRequestParams"/>
/// <seealso cref="UnsubscribeRequestParams"/>
/// <seealso href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</seealso>
public class ResourceUpdatedNotificationParams
{
    /// <summary>
    /// The URI of the resource that was updated. The URI can use any protocol; it is up to the server how to interpret it.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("uri")]
    public string? Uri { get; init; }
}
