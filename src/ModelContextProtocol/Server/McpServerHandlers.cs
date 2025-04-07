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
    /// Gets or sets the handler for completion complete requests.
    /// </summary>
    public Func<RequestContext<CompleteRequestParams>, CancellationToken, Task<CompleteResult>>? CompleteHandler { get; set; }

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
