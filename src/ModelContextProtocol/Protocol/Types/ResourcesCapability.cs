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
    public Func<RequestContext<ListResourceTemplatesRequestParams>, CancellationToken, ValueTask<ListResourceTemplatesResult>>? ListResourceTemplatesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ResourcesList"/> requests.
    /// </summary>
    /// <remarks>
    /// This handler responds to client requests for available resources and returns information about resources accessible through the server.
    /// The implementation should return a <see cref="ListResourcesResult"/> with the matching resources.
    /// </remarks>
    [JsonIgnore]
    public Func<RequestContext<ListResourcesRequestParams>, CancellationToken, ValueTask<ListResourcesResult>>? ListResourcesHandler { get; set; }

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
    public Func<RequestContext<ReadResourceRequestParams>, CancellationToken, ValueTask<ReadResourceResult>>? ReadResourceHandler { get; set; }

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
    public Func<RequestContext<SubscribeRequestParams>, CancellationToken, ValueTask<EmptyResult>>? SubscribeToResourcesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ResourcesUnsubscribe"/> requests.
    /// </summary>
    /// <remarks>
    /// When a client sends a <see cref="RequestMethods.ResourcesUnsubscribe"/> request, this handler is invoked with the resource URI
    /// to be unsubscribed from. The implementation should remove the client's registration for receiving updates
    /// about the specified resource.
    /// </remarks>
    [JsonIgnore]
    public Func<RequestContext<UnsubscribeRequestParams>, CancellationToken, ValueTask<EmptyResult>>? UnsubscribeFromResourcesHandler { get; set; }

    /// <summary>
    /// Gets or sets a collection of resources served by the server.
    /// </summary>
    /// <remarks>
    /// Resources will specified via <see cref="ResourceCollection"/> augment the <see cref="ListResourcesHandler"/> and
    /// <see cref="ReadResourceHandler"/>, if provided. ListResources requests will output information about every resource
    /// in <see cref="ResourceCollection"/> and then also any resources output by <see cref="ListResourcesHandler"/>, if it's
    /// non-<see langword="null"/>. ReadResource requests will first check <see cref="ResourceCollection"/> for the exact resource
    /// being requested, will then fall back to <see cref="ResourceTemplateCollection"/> if no exact match could be found, and if the resource 
    /// is still not found, any specified <see cref="ReadResourceHandler"/> will be invoked as a fallback.
    /// </remarks>
    [JsonIgnore]
    public McpServerPrimitiveCollection<McpServerResource>? ResourceCollection { get; set; }

    /// <summary>
    /// Gets or sets a collection of resource templates served by the server.
    /// </summary>
    /// <remarks>
    /// Resource templates will specified via <see cref="ResourceTemplateCollection"/> augment the <see cref="ListResourceTemplatesHandler"/> and
    /// <see cref="ReadResourceHandler"/>, if provided. ListResourceTemplates requests will output information about every resource template
    /// in <see cref="ResourceTemplateCollection"/> and then also any resources output by <see cref="ListResourceTemplatesHandler"/>, if it's
    /// non-<see langword="null"/>. ReadResource requests will first check <see cref="ResourceCollection"/> for the exact resource
    /// being requested, will then fall back to <see cref="ResourceTemplateCollection"/> if no exact match could be found, and if the resource 
    /// is still not found, any specified <see cref="ReadResourceHandler"/> will be invoked as a fallback.
    /// </remarks>
    [JsonIgnore]
    public McpServerPrimitiveCollection<McpServerResourceTemplate>? ResourceTemplateCollection { get; set; }
}