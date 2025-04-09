using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Server;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the resources capability configuration.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class ResourcesCapability
{
    /// <summary>
    /// Whether this server supports subscribing to resource updates.
    /// </summary>
    [JsonPropertyName("subscribe")]
    public bool? Subscribe { get; set; }

    /// <summary>
    /// Gets or sets whether this server supports notifications for changes to the resource list.
    /// When set to <see langword="true"/>, the server will send notifications using 
    /// <see cref="NotificationMethods.ResourceListChangedNotification"/> when resources are added, 
    /// removed, or modified. Clients can register handlers for these notifications to
    /// refresh their resource cache.
    /// </summary>
    /// <remarks>
    /// This capability enables clients to stay synchronized with server-side changes to available resources.
    /// The server will broadcast the <c>notifications/resources/list_changed</c> notification when resources change.
    /// </remarks>
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }

    /// <summary>
    /// Gets or sets the handler for list resource templates requests.
    /// This handler is called when clients request available resource templates that can be used
    /// to create resources within the Model Context Protocol server.
    /// </summary>
    /// <remarks>
    /// Resource templates define the structure and URI patterns for resources accessible in the system,
    /// allowing clients to discover available resource types and their access patterns.
    /// </remarks>
    /// <example>
    /// <code>
    /// capabilities.Resources = new ResourcesCapability
    /// {
    ///     ListResourceTemplatesHandler = (request, cancellationToken) =>
    ///     {
    ///         return Task.FromResult(new ListResourceTemplatesResult
    ///         {
    ///             ResourceTemplates = 
    ///             [
    ///                 new ResourceTemplate
    ///                 {
    ///                     Name = "Document",
    ///                     Description = "Document in the file system",
    ///                     UriTemplate = "files://documents/{id}"
    ///                 }
    ///             ]
    ///         });
    ///     }
    /// };
    /// </code>
    /// </example>
    [JsonIgnore]
    public Func<RequestContext<ListResourceTemplatesRequestParams>, CancellationToken, Task<ListResourceTemplatesResult>>? ListResourceTemplatesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for list resources requests. This handler responds to client requests 
    /// for available resources and returns information about resources accessible through the server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handler receives a <see cref="RequestContext{ListResourcesRequestParams}"/> containing 
    /// request parameters such as filters, pagination cursors, or other query options.
    /// </para>
    /// <para>
    /// The implementation should return a <see cref="ListResourcesResult"/> with the matching resources.
    /// Consider implementing pagination if the resource collection can be large.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// capabilities.Resources = new ResourcesCapability
    /// {
    ///     ListResourcesHandler = (context, cancellationToken) =>
    ///     {
    ///         // Access request parameters
    ///         var filter = context.Params?.Filter;
    ///         var cursor = context.Params?.Cursor;
    ///         
    ///         // Return matching resources
    ///         return Task.FromResult(new ListResourcesResult
    ///         {
    ///             Resources = 
    ///             [
    ///                 new Resource
    ///                 {
    ///                     Uri = "document://123",
    ///                     Name = "Sample document",
    ///                     Description = "A sample document resource"
    ///                 }
    ///             ]
    ///         });
    ///     }
    /// };
    /// </code>
    /// </example>
    [JsonIgnore]
    public Func<RequestContext<ListResourcesRequestParams>, CancellationToken, Task<ListResourcesResult>>? ListResourcesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for read resources requests. This handler is responsible for retrieving
    /// the content of a specific resource identified by its URI in the Model Context Protocol.
    /// </summary>
    /// <remarks>
    /// When a client sends a resources/read request, this handler is invoked with the resource URI.
    /// The handler should implement logic to locate and retrieve the requested resource, then return
    /// its contents in a ReadResourceResult object. If the resource cannot be found or accessed,
    /// the handler should throw an appropriate exception.
    /// </remarks>
    /// <example>
    /// <code>
    /// capabilities.Resources = new ResourcesCapability
    /// {
    ///     ReadResourceHandler = (context, cancellationToken) =>
    ///     {
    ///         var uri = context.Params?.Uri;
    ///         if (string.IsNullOrEmpty(uri))
    ///         {
    ///             throw new McpException("Resource URI is required");
    ///         }
    ///         
    ///         // Example: Handle different resource types by URI scheme
    ///         if (uri.StartsWith("document://"))
    ///         {
    ///             // Retrieve document content (implementation details would vary)
    ///             return Task.FromResult(new ReadResourceResult
    ///             {
    ///                 Contents = 
    ///                 [
    ///                     new TextResourceContents
    ///                     {
    ///                         Uri = uri,
    ///                         MimeType = "text/plain",
    ///                         Text = "Document content here"
    ///                     }
    ///                 ]
    ///             });
    ///         }
    ///         
    ///         throw new McpException($"Resource not found: {uri}");
    ///     }
    /// };
    /// </code>
    /// </example>
    [JsonIgnore]
    public Func<RequestContext<ReadResourceRequestParams>, CancellationToken, Task<ReadResourceResult>>? ReadResourceHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for subscribe to resources messages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When a client sends a resources/subscribe request, this handler is invoked with the resource URI
    /// to be subscribed to. The implementation should register the client's interest in receiving updates
    /// for the specified resource.
    /// </para>
    /// <para>
    /// Subscriptions allow clients to receive real-time notifications when resources change, without
    /// requiring polling. The server should maintain a list of subscriptions and notify clients when
    /// relevant resources are updated.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// capabilities.Resources = new ResourcesCapability
    /// {
    ///     SubscribeToResourcesHandler = (context, cancellationToken) =>
    ///     {
    ///         var resourceUri = context.Params?.Uri;
    ///         if (!string.IsNullOrEmpty(resourceUri))
    ///         {
    ///             // Store the subscription in a tracking system
    ///             _subscriptionManager.AddSubscription(context.ClientId, resourceUri);
    ///             
    ///             // Return success result
    ///             return Task.FromResult(new EmptyResult());
    ///         }
    ///         
    ///         throw new McpException("Invalid resource URI for subscription");
    ///     }
    /// };
    /// </code>
    /// </example>
    [JsonIgnore]
    public Func<RequestContext<SubscribeRequestParams>, CancellationToken, Task<EmptyResult>>? SubscribeToResourcesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for unsubscribe from resources messages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When a client sends a resources/unsubscribe request, this handler is invoked with the resource URI
    /// to be unsubscribed from. The implementation should remove the client's registration for receiving updates
    /// about the specified resource.
    /// </para>
    /// <para>
    /// This handler should be designed to work alongside the <see cref="SubscribeToResourcesHandler"/> to
    /// properly manage the lifecycle of client subscriptions to resources.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// capabilities.Resources = new ResourcesCapability
    /// {
    ///     UnsubscribeFromResourcesHandler = (context, cancellationToken) =>
    ///     {
    ///         var resourceUri = context.Params?.Uri;
    ///         if (!string.IsNullOrEmpty(resourceUri))
    ///         {
    ///             // Remove the subscription from the tracking system
    ///             _subscriptionManager.RemoveSubscription(context.ClientId, resourceUri);
    ///             
    ///             // Return success result
    ///             return Task.FromResult(new EmptyResult());
    ///         }
    ///         
    ///         throw new McpException("Invalid resource URI for unsubscription");
    ///     }
    /// };
    /// </code>
    /// </example>
    [JsonIgnore]
    public Func<RequestContext<UnsubscribeRequestParams>, CancellationToken, Task<EmptyResult>>? UnsubscribeFromResourcesHandler { get; set; }
}