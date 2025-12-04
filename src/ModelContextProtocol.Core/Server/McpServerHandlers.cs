using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides a container for handlers used in the creation of an MCP server.
/// </summary>
/// <remarks>
/// <para>
/// This class provides a centralized collection of delegates that implement various capabilities of the Model Context Protocol.
/// Each handler in this class corresponds to a specific endpoint in the Model Context Protocol and
/// is responsible for processing a particular type of message. The handlers are used to customize
/// the behavior of the MCP server by providing implementations for the various protocol operations.
/// </para>
/// <para>
/// When a client sends a message to the server, the appropriate handler is invoked to process it
/// according to the protocol specification. Which handler is selected
/// is done based on an ordinal, case-sensitive string comparison.
/// </para>
/// </remarks>
public sealed class McpServerHandlers
{
    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ToolsList"/> requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handler should return a list of available tools when requested by a client.
    /// It supports pagination through the cursor mechanism, where the client can make
    /// repeated calls with the cursor returned by the previous call to retrieve more tools.
    /// </para>
    /// <para>
    /// This handler works alongside any tools defined in the <see cref="McpServerTool"/> collection.
    /// Tools from both sources will be combined when returning results to clients.
    /// </para>
    /// </remarks>
    public McpRequestHandler<ListToolsRequestParams, ListToolsResult>? ListToolsHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ToolsCall"/> requests.
    /// </summary>
    /// <remarks>
    /// This handler is invoked when a client makes a call to a tool that isn't found in the <see cref="McpServerTool"/> collection.
    /// The handler should implement logic to execute the requested tool and return appropriate results.
    /// </remarks>
    public McpRequestHandler<CallToolRequestParams, CallToolResult>? CallToolHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.PromptsList"/> requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handler should return a list of available prompts when requested by a client.
    /// It supports pagination through the cursor mechanism, where the client can make
    /// repeated calls with the cursor returned by the previous call to retrieve more prompts.
    /// </para>
    /// <para>
    /// This handler works alongside any prompts defined in the <see cref="McpServerPrompt"/> collection.
    /// Prompts from both sources will be combined when returning results to clients.
    /// </para>
    /// </remarks>
    public McpRequestHandler<ListPromptsRequestParams, ListPromptsResult>? ListPromptsHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.PromptsGet"/> requests.
    /// </summary>
    /// <remarks>
    /// This handler is invoked when a client requests details for a specific prompt that isn't found in the <see cref="McpServerPrompt"/> collection.
    /// The handler should implement logic to fetch or generate the requested prompt and return appropriate results.
    /// </remarks>
    public McpRequestHandler<GetPromptRequestParams, GetPromptResult>? GetPromptHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ResourcesTemplatesList"/> requests.
    /// </summary>
    /// <remarks>
    /// The handler should return a list of available resource templates when requested by a client.
    /// It supports pagination through the cursor mechanism, where the client can make
    /// repeated calls with the cursor returned by the previous call to retrieve more resource templates.
    /// </remarks>
    public McpRequestHandler<ListResourceTemplatesRequestParams, ListResourceTemplatesResult>? ListResourceTemplatesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ResourcesList"/> requests.
    /// </summary>
    /// <remarks>
    /// The handler should return a list of available resources when requested by a client.
    /// It supports pagination through the cursor mechanism, where the client can make
    /// repeated calls with the cursor returned by the previous call to retrieve more resources.
    /// </remarks>
    public McpRequestHandler<ListResourcesRequestParams, ListResourcesResult>? ListResourcesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ResourcesRead"/> requests.
    /// </summary>
    /// <remarks>
    /// This handler is invoked when a client requests the content of a specific resource identified by its URI.
    /// The handler should implement logic to locate and retrieve the requested resource.
    /// </remarks>
    public McpRequestHandler<ReadResourceRequestParams, ReadResourceResult>? ReadResourceHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.CompletionComplete"/> requests.
    /// </summary>
    /// <remarks>
    /// This handler provides auto-completion suggestions for prompt arguments or resource references in the Model Context Protocol.
    /// The handler processes auto-completion requests, returning a list of suggestions based on the
    /// reference type and current argument value.
    /// </remarks>
    public McpRequestHandler<CompleteRequestParams, CompleteResult>? CompleteHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ResourcesSubscribe"/> requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This handler is invoked when a client wants to receive notifications about changes to specific resources or resource patterns.
    /// The handler should implement logic to register the client's interest in the specified resources
    /// and set up the necessary infrastructure to send notifications when those resources change.
    /// </para>
    /// <para>
    /// After a successful subscription, the server should send resource change notifications to the client
    /// whenever a relevant resource is created, updated, or deleted.
    /// </para>
    /// </remarks>
    public McpRequestHandler<SubscribeRequestParams, EmptyResult>? SubscribeToResourcesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ResourcesUnsubscribe"/> requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This handler is invoked when a client wants to stop receiving notifications about previously subscribed resources.
    /// The handler should implement logic to remove the client's subscriptions to the specified resources
    /// and clean up any associated resources.
    /// </para>
    /// <para>
    /// After a successful unsubscription, the server should no longer send resource change notifications
    /// to the client for the specified resources.
    /// </para>
    /// </remarks>
    public McpRequestHandler<UnsubscribeRequestParams, EmptyResult>? UnsubscribeFromResourcesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.LoggingSetLevel"/> requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This handler processes <see cref="RequestMethods.LoggingSetLevel"/> requests from clients. When set, it enables
    /// clients to control which log messages they receive by specifying a minimum severity threshold.
    /// </para>
    /// <para>
    /// After handling a level change request, the server typically begins sending log messages
    /// at or above the specified level to the client as notifications/message notifications.
    /// </para>
    /// </remarks>
    public McpRequestHandler<SetLevelRequestParams, EmptyResult>? SetLoggingLevelHandler { get; set; }

    /// <summary>Gets or sets notification handlers to register with the server.</summary>
    /// <remarks>
    /// <para>
    /// When constructed, the server will enumerate these handlers, which may contain multiple handlers per notification method key, once.
    /// The server will not re-enumerate the sequence after initialization.
    /// </para>
    /// <para>
    /// Notification handlers allow the server to respond to client-sent notifications for specific methods.
    /// Each key in the collection is a notification method name, and each value is a callback that will be invoked
    /// when a notification with that method is received.
    /// </para>
    /// <para>
    /// Handlers provided via <see cref="NotificationHandlers"/> will be registered with the server for the lifetime of the server.
    /// For transient handlers, <see cref="McpSession.RegisterNotificationHandler"/> may be used to register a handler that can
    /// then be unregistered by disposing of the <see cref="IAsyncDisposable"/> returned from the method.
    /// </para>
    /// </remarks>
    public IEnumerable<KeyValuePair<string, Func<JsonRpcNotification, CancellationToken, ValueTask>>>? NotificationHandlers { get; set; }
}
