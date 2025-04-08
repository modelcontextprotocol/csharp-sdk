namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Sent from the client to request real-time notifications from the server whenever a particular resource changes.
/// This enables clients to maintain up-to-date representations of resources without polling.
/// </summary>
/// <remarks>
/// <para>
/// The subscription mechanism allows clients to be notified about changes to specific resources
/// identified by their URI. When a subscribed resource changes, the server sends a notification
/// to the client with the updated resource information.
/// </para>
/// <para>
/// Subscriptions remain active until explicitly canceled using <see cref="UnsubscribeRequestParams"/>
/// or until the connection is terminated.
/// </para>
/// <para>
/// The server may refuse or limit subscriptions based on its capabilities or resource constraints.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Subscribe to changes on a specific resource
/// await client.SubscribeToResourcesAsync("resource://documents/123");
/// 
/// // Handle resource change notifications in your client
/// client.ResourceChanged += (sender, e) => 
/// {
///     Console.WriteLine($"Resource {e.Uri} has changed");
///     // Refresh your UI or data model with the updated resource
/// };
/// 
/// // Later, unsubscribe when no longer needed
/// await client.UnsubscribeFromResourcesAsync("resource://documents/123");
/// </code>
/// </example>
/// <seealso cref="UnsubscribeRequestParams"/>
/// <seealso cref="ResourcesCapability"/>
/// <seealso href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</seealso>
public class SubscribeRequestParams : RequestParams
{
    /// <summary>
    /// The URI of the resource to subscribe to. The URI can use any protocol; it is up to the server how to interpret it.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("uri")]
    public string? Uri { get; init; }
}
