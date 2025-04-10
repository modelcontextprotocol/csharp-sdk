using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Server;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the resources capability configuration.
/// </summary>
/// <remarks>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public class ResourcesCapability
{
    /// <summary>
    /// Gets or sets whether this server supports subscribing to resource updates.
    /// </summary>
    [JsonPropertyName("subscribe")]
    public bool? Subscribe { get; set; }

    /// <summary>
    /// Gets or sets whether this server supports notifications for changes to the resource list.
    /// </summary>
    /// <remarks>
    /// When set to <see langword="true"/>, the server will send notifications using 
    /// <see cref="NotificationMethods.ResourceListChangedNotification"/> when resources are added, 
    /// removed, or modified. Clients can register handlers for these notifications to
    /// refresh their resource cache.
    /// </remarks>
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ResourcesTemplatesList"/> requests.
    /// </summary>
    /// <remarks>
    /// This handler is called when clients request available resource templates that can be used
    /// to create resources within the Model Context Protocol server.
    /// Resource templates define the structure and URI patterns for resources accessible in the system,
    /// allowing clients to discover available resource types and their access patterns.
    /// </remarks>
    [JsonIgnore]
    public Func<RequestContext<ListResourceTemplatesRequestParams>, CancellationToken, Task<ListResourceTemplatesResult>>? ListResourceTemplatesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ResourcesList"/> requests.
    /// </summary>
    /// <remarks>
    /// This handler responds to client requests for available resources and returns information about resources accessible through the server.
    /// The implementation should return a <see cref="ListResourcesResult"/> with the matching resources.
    /// </remarks>
    [JsonIgnore]
    public Func<RequestContext<ListResourcesRequestParams>, CancellationToken, Task<ListResourcesResult>>? ListResourcesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ResourcesRead"/> requests.
    /// </summary>
    /// <remarks>
    /// This handler is responsible for retrieving the content of a specific resource identified by its URI in the Model Context Protocol.
    /// When a client sends a resources/read request, this handler is invoked with the resource URI.
    /// The handler should implement logic to locate and retrieve the requested resource, then return
    /// its contents in a ReadResourceResult object.
    /// </remarks>
    [JsonIgnore]
    public Func<RequestContext<ReadResourceRequestParams>, CancellationToken, Task<ReadResourceResult>>? ReadResourceHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ResourcesSubscribe"/> requests.
    /// </summary>
    /// <remarks>
    /// When a client sends a <see cref="RequestMethods.ResourcesSubscribe"/> request, this handler is invoked with the resource URI
    /// to be subscribed to. The implementation should register the client's interest in receiving updates
    /// for the specified resource.
    /// Subscriptions allow clients to receive real-time notifications when resources change, without
    /// requiring polling.
    /// </remarks>
    [JsonIgnore]
    public Func<RequestContext<SubscribeRequestParams>, CancellationToken, Task<EmptyResult>>? SubscribeToResourcesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ResourcesUnsubscribe"/> requests.
    /// </summary>
    /// <remarks>
    /// When a client sends a <see cref="RequestMethods.ResourcesUnsubscribe"/> request, this handler is invoked with the resource URI
    /// to be unsubscribed from. The implementation should remove the client's registration for receiving updates
    /// about the specified resource.
    /// </remarks>
    [JsonIgnore]
    public Func<RequestContext<UnsubscribeRequestParams>, CancellationToken, Task<EmptyResult>>? UnsubscribeFromResourcesHandler { get; set; }
}