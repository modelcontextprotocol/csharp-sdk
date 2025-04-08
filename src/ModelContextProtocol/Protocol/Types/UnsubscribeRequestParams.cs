namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Sent from the client to cancel resource update notifications from the server for a specific resource.
/// </summary>
/// <remarks>
/// <para>
/// After a client has subscribed to resource updates using <see cref="SubscribeRequestParams"/>, 
/// this message can be sent to stop receiving notifications for a specific resource. 
/// This is useful for conserving resources and network bandwidth when 
/// the client no longer needs to track changes to a particular resource.
/// </para>
/// <para>
/// The unsubscribe operation is idempotent, meaning it can be called multiple times 
/// for the same resource without causing errors, even if there is no active subscription.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // First subscribe to a resource
/// await client.SubscribeToResourceAsync("resource://documents/123", cancellationToken);
/// 
/// // When updates are no longer needed
/// await client.UnsubscribeFromResourceAsync("resource://documents/123", cancellationToken);
/// 
/// // Server-side handler implementation
/// server.WithUnsubscribeFromResourcesHandler((context, cancellationToken) =>
/// {
///     var resourceUri = context.Params?.Uri;
///     if (!string.IsNullOrEmpty(resourceUri))
///     {
///         // Remove the subscription from the server's tracking system
///         _subscriptionManager.RemoveSubscription(context.ClientId, resourceUri);
///     }
///     return Task.FromResult(new EmptyResult());
/// });
/// </code>
/// </example>
/// <seealso cref="SubscribeRequestParams"/>
/// <seealso cref="ResourceUpdatedNotificationParams"/>
/// <seealso href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</seealso>
public class UnsubscribeRequestParams : RequestParams
{
    /// <summary>
    /// The URI of the resource to unsubscribe from. The URI can use any protocol; it is up to the server how to interpret it.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("uri")]
    public string? Uri { get; init; }
}
