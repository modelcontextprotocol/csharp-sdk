using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides a container for handlers used in the creation of an MCP server.
/// </summary>
/// <remarks>
/// <para>
/// This class provides a centralized collection of delegates that implement various capabilities of the Model Context Protocol.
/// Each handler in this class corresponds to a specific endpoint in the Model Context Protocol and
/// is responsible for processing a particular type of request. The handlers are used to customize
/// the behavior of the MCP server by providing implementations for the various protocol operations.
/// </para>
/// <para>
/// Handlers can be configured individually using the extension methods in <see cref="McpServerBuilderExtensions"/>
/// such as <see cref="McpServerBuilderExtensions.WithListToolsHandler"/> and
/// <see cref="McpServerBuilderExtensions.WithCallToolHandler"/>.
/// </para>
/// <para>
/// When a client sends a request to the server, the appropriate handler is invoked to process the
/// request and produce a response according to the protocol specification. Which handler is selected
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
    public Func<RequestContext<ListToolsRequestParams>, CancellationToken, ValueTask<ListToolsResult>>? ListToolsHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ToolsCall"/> requests.
    /// </summary>
    /// <remarks>
    /// This handler is invoked when a client makes a call to a tool that isn't found in the <see cref="McpServerTool"/> collection.
    /// The handler should implement logic to execute the requested tool and return appropriate results.
    /// </remarks>
    public Func<RequestContext<CallToolRequestParams>, CancellationToken, ValueTask<CallToolResult>>? CallToolHandler { get; set; }

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
    public Func<RequestContext<ListPromptsRequestParams>, CancellationToken, ValueTask<ListPromptsResult>>? ListPromptsHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.PromptsGet"/> requests.
    /// </summary>
    /// <remarks>
    /// This handler is invoked when a client requests details for a specific prompt that isn't found in the <see cref="McpServerPrompt"/> collection.
    /// The handler should implement logic to fetch or generate the requested prompt and return appropriate results.
    /// </remarks>
    public Func<RequestContext<GetPromptRequestParams>, CancellationToken, ValueTask<GetPromptResult>>? GetPromptHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ResourcesTemplatesList"/> requests.
    /// </summary>
    /// <remarks>
    /// The handler should return a list of available resource templates when requested by a client.
    /// It supports pagination through the cursor mechanism, where the client can make
    /// repeated calls with the cursor returned by the previous call to retrieve more resource templates.
    /// </remarks>
    public Func<RequestContext<ListResourceTemplatesRequestParams>, CancellationToken, ValueTask<ListResourceTemplatesResult>>? ListResourceTemplatesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ResourcesList"/> requests.
    /// </summary>
    /// <remarks>
    /// The handler should return a list of available resources when requested by a client.
    /// It supports pagination through the cursor mechanism, where the client can make
    /// repeated calls with the cursor returned by the previous call to retrieve more resources.
    /// </remarks>
    public Func<RequestContext<ListResourcesRequestParams>, CancellationToken, ValueTask<ListResourcesResult>>? ListResourcesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ResourcesRead"/> requests.
    /// </summary>
    /// <remarks>
    /// This handler is invoked when a client requests the content of a specific resource identified by its URI.
    /// The handler should implement logic to locate and retrieve the requested resource.
    /// </remarks>
    public Func<RequestContext<ReadResourceRequestParams>, CancellationToken, ValueTask<ReadResourceResult>>? ReadResourceHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.CompletionComplete"/> requests.
    /// </summary>
    /// <remarks>
    /// This handler provides auto-completion suggestions for prompt arguments or resource references in the Model Context Protocol.
    /// The handler processes auto-completion requests, returning a list of suggestions based on the 
    /// reference type and current argument value.
    /// </remarks>
    public Func<RequestContext<CompleteRequestParams>, CancellationToken, ValueTask<CompleteResult>>? CompleteHandler { get; set; }

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
    public Func<RequestContext<SubscribeRequestParams>, CancellationToken, ValueTask<EmptyResult>>? SubscribeToResourcesHandler { get; set; }

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
    public Func<RequestContext<UnsubscribeRequestParams>, CancellationToken, ValueTask<EmptyResult>>? UnsubscribeFromResourcesHandler { get; set; }

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
    public Func<RequestContext<SetLevelRequestParams>, CancellationToken, ValueTask<EmptyResult>>? SetLoggingLevelHandler { get; set; }

    /// <summary>
    /// Overwrite any handlers in McpServerOptions with non-null handlers from this instance.
    /// </summary>
    /// <param name="options"></param>
    /// <returns></returns>
    internal void OverwriteWithSetHandlers(McpServerOptions options)
    {
        PromptsCapability? promptsCapability = options.Capabilities?.Prompts;
        if (ListPromptsHandler is not null || GetPromptHandler is not null)
        {
            promptsCapability ??= new();
            promptsCapability.ListPromptsHandler = ListPromptsHandler ?? promptsCapability.ListPromptsHandler;
            promptsCapability.GetPromptHandler = GetPromptHandler ?? promptsCapability.GetPromptHandler;
        }

        ResourcesCapability? resourcesCapability = options.Capabilities?.Resources;
        if (ListResourcesHandler is not null ||
            ReadResourceHandler is not null)
        {
            resourcesCapability ??= new();
            resourcesCapability.ListResourceTemplatesHandler = ListResourceTemplatesHandler ?? resourcesCapability.ListResourceTemplatesHandler;
            resourcesCapability.ListResourcesHandler = ListResourcesHandler ?? resourcesCapability.ListResourcesHandler;
            resourcesCapability.ReadResourceHandler = ReadResourceHandler ?? resourcesCapability.ReadResourceHandler;

            if (SubscribeToResourcesHandler is not null || UnsubscribeFromResourcesHandler is not null)
            {
                resourcesCapability.SubscribeToResourcesHandler = SubscribeToResourcesHandler ?? resourcesCapability.SubscribeToResourcesHandler;
                resourcesCapability.UnsubscribeFromResourcesHandler = UnsubscribeFromResourcesHandler ?? resourcesCapability.UnsubscribeFromResourcesHandler;
                resourcesCapability.Subscribe = true;
            }
        }

        ToolsCapability? toolsCapability = options.Capabilities?.Tools;
        if (ListToolsHandler is not null || CallToolHandler is not null)
        {
            toolsCapability ??= new();
            toolsCapability.ListToolsHandler = ListToolsHandler ?? toolsCapability.ListToolsHandler;
            toolsCapability.CallToolHandler = CallToolHandler ?? toolsCapability.CallToolHandler;
        }

        LoggingCapability? loggingCapability = options.Capabilities?.Logging;
        if (SetLoggingLevelHandler is not null)
        {
            loggingCapability ??= new();
            loggingCapability.SetLoggingLevelHandler = SetLoggingLevelHandler;
        }

        CompletionsCapability? completionsCapability = options.Capabilities?.Completions;
        if (CompleteHandler is not null)
        {
            completionsCapability ??= new();
            completionsCapability.CompleteHandler = CompleteHandler;
        }

        options.Capabilities ??= new();
        options.Capabilities.Prompts = promptsCapability;
        options.Capabilities.Resources = resourcesCapability;
        options.Capabilities.Tools = toolsCapability;
        options.Capabilities.Logging = loggingCapability;
        options.Capabilities.Completions = completionsCapability;
    }
}
