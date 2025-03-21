using ModelContextProtocol.Protocol.Types;

namespace ModelContextProtocol.Server;

/// <summary>
/// Container for handlers used in the creation of an MCP server.
/// </summary>
public sealed class McpServerHandlers
{
    /// <summary>
    /// Gets or sets the handler for list tools requests.
    /// </summary>
    public Func<RequestContext<ListToolsRequestParams>, CancellationToken, Task<ListToolsResult>>? ListToolsHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for call tool requests.
    /// </summary>
    public Func<RequestContext<CallToolRequestParams>, CancellationToken, Task<CallToolResponse>>? CallToolHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for list prompts requests.
    /// </summary>
    public Func<RequestContext<ListPromptsRequestParams>, CancellationToken, Task<ListPromptsResult>>? ListPromptsHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for get prompt requests.
    /// </summary>
    public Func<RequestContext<GetPromptRequestParams>, CancellationToken, Task<GetPromptResult>>? GetPromptHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for list resource templates requests.
    /// </summary>
    public Func<RequestContext<ListResourceTemplatesRequestParams>, CancellationToken, Task<ListResourceTemplatesResult>>? ListResourceTemplatesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for list resources requests.
    /// </summary>
    public Func<RequestContext<ListResourcesRequestParams>, CancellationToken, Task<ListResourcesResult>>? ListResourcesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for read resources requests.
    /// </summary>
    public Func<RequestContext<ReadResourceRequestParams>, CancellationToken, Task<ReadResourceResult>>? ReadResourceHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for get completion requests.
    /// </summary>
    public Func<RequestContext<CompleteRequestParams>, CancellationToken, Task<CompleteResult>>? GetCompletionHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for subscribe to resources messages.
    /// </summary>
    public Func<RequestContext<SubscribeRequestParams>, CancellationToken, Task<EmptyResult>>? SubscribeToResourcesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for unsubscribe from resources messages.
    /// </summary>
    public Func<RequestContext<UnsubscribeRequestParams>, CancellationToken, Task<EmptyResult>>? UnsubscribeFromResourcesHandler { get; set; }

    /// <summary>
    /// Get or sets the handler for set logging level requests.
    /// </summary>
    public Func<RequestContext<SetLevelRequestParams>, CancellationToken, Task<EmptyResult>>? SetLoggingLevelHandler { get; set; }

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
            promptsCapability = promptsCapability is null ?
                new()
                {
                    ListPromptsHandler = ListPromptsHandler,
                    GetPromptHandler = GetPromptHandler,
                } :
                promptsCapability with
                {
                    ListPromptsHandler = ListPromptsHandler ?? promptsCapability.ListPromptsHandler,
                    GetPromptHandler = GetPromptHandler ?? promptsCapability.GetPromptHandler,
                };
        }

        ResourcesCapability? resourcesCapability = options.Capabilities?.Resources;
        if (ListResourcesHandler is not null ||
            ReadResourceHandler is not null)
        {
            resourcesCapability = resourcesCapability is null ?
                new()
                {
                    ListResourceTemplatesHandler = ListResourceTemplatesHandler,
                    ListResourcesHandler = ListResourcesHandler,
                    ReadResourceHandler = ReadResourceHandler
                } :
                resourcesCapability with
                {
                    ListResourceTemplatesHandler = ListResourceTemplatesHandler ?? resourcesCapability.ListResourceTemplatesHandler,
                    ListResourcesHandler = ListResourcesHandler ?? resourcesCapability.ListResourcesHandler,
                    ReadResourceHandler = ReadResourceHandler ?? resourcesCapability.ReadResourceHandler
                };

            if (SubscribeToResourcesHandler is not null || UnsubscribeFromResourcesHandler is not null)
            {
                resourcesCapability = resourcesCapability with
                {
                    SubscribeToResourcesHandler = SubscribeToResourcesHandler ?? resourcesCapability.SubscribeToResourcesHandler,
                    UnsubscribeFromResourcesHandler = UnsubscribeFromResourcesHandler ?? resourcesCapability.UnsubscribeFromResourcesHandler,
                    Subscribe = true
                };
            }
        }

        ToolsCapability? toolsCapability = options.Capabilities?.Tools;
        if (ListToolsHandler is not null || CallToolHandler is not null)
        {
            toolsCapability = toolsCapability is null ?
                new()
                {
                    ListToolsHandler = ListToolsHandler,
                    CallToolHandler = CallToolHandler,
                } :
                toolsCapability with
                {
                    ListToolsHandler = ListToolsHandler ?? toolsCapability.ListToolsHandler,
                    CallToolHandler = CallToolHandler ?? toolsCapability.CallToolHandler,
                };
        }

        LoggingCapability? loggingCapability = options.Capabilities?.Logging;
        if (SetLoggingLevelHandler is not null)
        {
            loggingCapability = loggingCapability is null ?
                new()
                {
                    SetLoggingLevelHandler = SetLoggingLevelHandler,
                } :
                loggingCapability with
                {
                    SetLoggingLevelHandler = SetLoggingLevelHandler ?? loggingCapability.SetLoggingLevelHandler,
                };
        }

        options.Capabilities = options.Capabilities is null ?
            new()
            {
                Prompts = promptsCapability,
                Resources = resourcesCapability,
                Tools = toolsCapability,
            } :
            options.Capabilities with
            {
                Prompts = promptsCapability,
                Resources = resourcesCapability,
                Tools = toolsCapability,
            };

        options.GetCompletionHandler = GetCompletionHandler ?? options.GetCompletionHandler;
    }
}
