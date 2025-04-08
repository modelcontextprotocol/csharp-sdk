namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Sent from the client to request cancellation of resources/updated notifications from the server.
/// This is used to stop receiving notifications for previously subscribed resources.
/// </summary>
/// <remarks>
/// <para>
/// When a client no longer needs to be notified about changes to a specific resource,
/// it can send an unsubscribe request with this parameter type. The server will then
/// stop sending notifications for that resource.
/// </para>
/// <para>
/// This is typically used in conjunction with the resource subscription feature,
/// where clients can register to receive real-time notifications about resource changes.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Client sending an unsubscribe request
/// var unsubscribeParams = new UnsubscribeFromResourceRequestParams
/// {
///     Uri = "resource://documents/123"
/// };
/// 
/// await client.SendRequestAsync(
///     RequestMethods.ResourcesUnsubscribe,
///     unsubscribeParams,
///     cancellationToken);
/// </code>
/// </example>
/// <seealso cref="SubscribeRequestParams"/>
/// <seealso cref="ResourceUpdatedNotificationParams"/>
/// <seealso href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</seealso>
public class UnsubscribeFromResourceRequestParams
{
    /// <summary>
    /// The URI of the resource to unsubscribe from. The server will stop sending
    /// notifications about changes to this resource after processing the request.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("uri")]
    public string? Uri { get; init; }
}
