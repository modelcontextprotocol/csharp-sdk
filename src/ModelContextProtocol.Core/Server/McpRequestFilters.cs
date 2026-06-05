using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides grouped request-specific filter collections.
/// </summary>
public sealed class McpRequestFilters
{
    /// <summary>
    /// Gets or sets the filters for the list-tools handler pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These filters wrap handlers that return a list of available tools when requested by a client.
    /// The filters can modify, log, or perform additional operations on requests and responses for
    /// <see cref="RequestMethods.ToolsList"/> requests. It supports pagination through the cursor mechanism,
    /// where the client can make repeated calls with the cursor returned by the previous call to retrieve more tools.
    /// </para>
    /// <para>
    /// These filters work alongside any tools defined in the <see cref="McpServerTool"/> collection.
    /// Tools from both sources will be combined when returning results to clients.
    /// </para>
    /// </remarks>
    public IList<McpRequestFilter<ListToolsRequestParams, ListToolsResult>> ListToolsFilters
    {
        get => field ??= [];
        set
        {
            Throw.IfNull(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the filters for the call-tool handler pipeline.
    /// </summary>
    /// <remarks>
    /// These filters wrap handlers that are invoked when a client makes a call to a tool that isn't found in the <see cref="McpServerTool"/> collection.
    /// The filters can modify, log, or perform additional operations on requests and responses for
    /// <see cref="RequestMethods.ToolsCall"/> requests. The handler should implement logic to execute the requested tool and return appropriate results.
    /// </remarks>
    public IList<McpRequestFilter<CallToolRequestParams, CallToolResult>> CallToolFilters
    {
        get => field ??= [];
        set
        {
            Throw.IfNull(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the filters for the list-prompts handler pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These filters wrap handlers that return a list of available prompts when requested by a client.
    /// The filters can modify, log, or perform additional operations on requests and responses for
    /// <see cref="RequestMethods.PromptsList"/> requests. It supports pagination through the cursor mechanism,
    /// where the client can make repeated calls with the cursor returned by the previous call to retrieve more prompts.
    /// </para>
    /// <para>
    /// These filters work alongside any prompts defined in the <see cref="McpServerPrompt"/> collection.
    /// Prompts from both sources will be combined when returning results to clients.
    /// </para>
    /// </remarks>
    public IList<McpRequestFilter<ListPromptsRequestParams, ListPromptsResult>> ListPromptsFilters
    {
        get => field ??= [];
        set
        {
            Throw.IfNull(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the filters for the get-prompt handler pipeline.
    /// </summary>
    /// <remarks>
    /// These filters wrap handlers that are invoked when a client requests details for a specific prompt that isn't found in the <see cref="McpServerPrompt"/> collection.
    /// The filters can modify, log, or perform additional operations on requests and responses for
    /// <see cref="RequestMethods.PromptsGet"/> requests. The handler should implement logic to fetch or generate the requested prompt and return appropriate results.
    /// </remarks>
    public IList<McpRequestFilter<GetPromptRequestParams, GetPromptResult>> GetPromptFilters
    {
        get => field ??= [];
        set
        {
            Throw.IfNull(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the filters for the list-resource-templates handler pipeline.
    /// </summary>
    /// <remarks>
    /// These filters wrap handlers that return a list of available resource templates when requested by a client.
    /// The filters can modify, log, or perform additional operations on requests and responses for
    /// <see cref="RequestMethods.ResourcesTemplatesList"/> requests. It supports pagination through the cursor mechanism,
    /// where the client can make repeated calls with the cursor returned by the previous call to retrieve more resource templates.
    /// </remarks>
    public IList<McpRequestFilter<ListResourceTemplatesRequestParams, ListResourceTemplatesResult>> ListResourceTemplatesFilters
    {
        get => field ??= [];
        set
        {
            Throw.IfNull(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the filters for the list-resources handler pipeline.
    /// </summary>
    /// <remarks>
    /// These filters wrap handlers that return a list of available resources when requested by a client.
    /// The filters can modify, log, or perform additional operations on requests and responses for
    /// <see cref="RequestMethods.ResourcesList"/> requests. It supports pagination through the cursor mechanism,
    /// where the client can make repeated calls with the cursor returned by the previous call to retrieve more resources.
    /// </remarks>
    public IList<McpRequestFilter<ListResourcesRequestParams, ListResourcesResult>> ListResourcesFilters
    {
        get => field ??= [];
        set
        {
            Throw.IfNull(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the filters for the read-resource handler pipeline.
    /// </summary>
    /// <remarks>
    /// These filters wrap handlers that are invoked when a client requests the content of a specific resource identified by its URI.
    /// The filters can modify, log, or perform additional operations on requests and responses for
    /// <see cref="RequestMethods.ResourcesRead"/> requests. The handler should implement logic to locate and retrieve the requested resource.
    /// </remarks>
    public IList<McpRequestFilter<ReadResourceRequestParams, ReadResourceResult>> ReadResourceFilters
    {
        get => field ??= [];
        set
        {
            Throw.IfNull(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the filters for the complete-handler pipeline.
    /// </summary>
    /// <remarks>
    /// These filters wrap handlers that provide auto-completion suggestions for prompt arguments or resource references in the Model Context Protocol.
    /// The filters can modify, log, or perform additional operations on requests and responses for
    /// <see cref="RequestMethods.CompletionComplete"/> requests. The handler processes auto-completion requests, returning a list of suggestions based on the
    /// reference type and current argument value.
    /// </remarks>
    public IList<McpRequestFilter<CompleteRequestParams, CompleteResult>> CompleteFilters
    {
        get => field ??= [];
        set
        {
            Throw.IfNull(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the filters for the subscribe-to-resources handler pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These filters wrap handlers that are invoked when a client wants to receive notifications about changes to specific resources or resource patterns.
    /// The filters can modify, log, or perform additional operations on requests and responses for
    /// <see cref="RequestMethods.ResourcesSubscribe"/> requests. The handler should implement logic to register the client's interest in the specified resources
    /// and set up the necessary infrastructure to send notifications when those resources change.
    /// </para>
    /// <para>
    /// After a successful subscription, the server should send resource change notifications to the client
    /// whenever a relevant resource is created, updated, or deleted.
    /// </para>
    /// </remarks>
    public IList<McpRequestFilter<SubscribeRequestParams, EmptyResult>> SubscribeToResourcesFilters
    {
        get => field ??= [];
        set
        {
            Throw.IfNull(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the filters for the unsubscribe-from-resources handler pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These filters wrap handlers that are invoked when a client wants to stop receiving notifications about previously subscribed resources.
    /// The filters can modify, log, or perform additional operations on requests and responses for
    /// <see cref="RequestMethods.ResourcesUnsubscribe"/> requests. The handler should implement logic to remove the client's subscriptions to the specified resources
    /// and clean up any associated resources.
    /// </para>
    /// <para>
    /// After a successful unsubscription, the server should no longer send resource change notifications
    /// to the client for the specified resources.
    /// </para>
    /// </remarks>
    public IList<McpRequestFilter<UnsubscribeRequestParams, EmptyResult>> UnsubscribeFromResourcesFilters
    {
        get => field ??= [];
        set
        {
            Throw.IfNull(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the filters for the set-logging-level handler pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These filters wrap handlers that process <see cref="RequestMethods.LoggingSetLevel"/> requests from clients. When set, it enables
    /// clients to control which log messages they receive by specifying a minimum severity threshold.
    /// The filters can modify, log, or perform additional operations on requests and responses for
    /// <see cref="RequestMethods.LoggingSetLevel"/> requests.
    /// </para>
    /// <para>
    /// After handling a level change request, the server typically begins sending log messages
    /// at or above the specified level to the client as notifications/message notifications.
    /// </para>
    /// </remarks>
    public IList<McpRequestFilter<SetLevelRequestParams, EmptyResult>> SetLoggingLevelFilters
    {
        get => field ??= [];
        set
        {
            Throw.IfNull(value);
            field = value;
        }
    }
}
