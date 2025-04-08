using ModelContextProtocol.Protocol.Types;

namespace ModelContextProtocol.Server;

/// <summary>
/// Container for handlers used in the creation of an MCP server. This class provides a centralized
/// collection of delegates that implement various capabilities of the Model Context Protocol.
/// </summary>
/// <remarks>
/// <para>
/// Each handler in this class corresponds to a specific endpoint in the Model Context Protocol and
/// is responsible for processing a particular type of request. The handlers are used to customize
/// the behavior of the MCP server by providing implementations for the various protocol operations.
/// </para>
/// <para>
/// Handlers can be configured individually using the extension methods in <see cref="Configuration.McpServerBuilderExtensions"/>
/// such as <c>WithListToolsHandler</c>, <c>WithCallToolHandler</c>, etc.
/// </para>
/// <para>
/// When a client sends a request to the server, the appropriate handler is invoked to process the
/// request and produce a response according to the protocol specification.
/// </para>
/// </remarks>
public sealed class McpServerHandlers
{
    /// <summary>
    /// Gets or sets the handler for list tools requests.
    /// </summary>
    /// <remarks>
    /// The handler should return a list of available tools when requested by a client.
    /// It supports pagination through the cursor mechanism, where the client can make
    /// repeated calls with the cursor returned by the previous call to retrieve more tools.
    /// 
    /// This handler works alongside any tools defined in the <see cref="McpServerTool"/> collection.
    /// Tools from both sources will be combined when returning results to clients.
    /// </remarks>
    /// <example>
    /// <code>
    /// handlers.ListToolsHandler = (request, cancellationToken) =>
    /// {
    ///     var cursor = request.Params?.Cursor;
    ///     // Handle pagination based on cursor value
    ///     return Task.FromResult(new ListToolsResult
    ///     {
    ///         Tools = [new() { Name = "sample-tool", Description = "A sample tool" }],
    ///         NextCursor = "next-page-token" // For pagination
    ///     });
    /// };
    /// </code>
    /// </example>
    public Func<RequestContext<ListToolsRequestParams>, CancellationToken, Task<ListToolsResult>>? ListToolsHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for call tool requests. This handler is invoked when a client makes a call to a tool
    /// that isn't found in the <see cref="McpServerTool"/> collection.
    /// </summary>
    /// <remarks>
    /// The handler should implement logic to execute the requested tool and return appropriate results.
    /// When registered using <see cref="McpServerBuilderExtensions.WithCallToolHandler"/>, it enables support for custom tool execution logic.
    /// </remarks>
    /// <example>
    /// <code>
    /// handlers.CallToolHandler = async (request, cancellationToken) =>
    /// {
    ///     if (request.Params?.Name == "echo")
    ///     {
    ///         var message = request.Params?.Arguments?["message"].ToString();
    ///         return new CallToolResponse()
    ///         {
    ///             Content = [new Content() { Text = "Echo: " + message, Type = "text" }]
    ///         };
    ///     }
    ///     throw new McpException($"Unknown tool: {request.Params?.Name}");
    /// };
    /// </code>
    /// </example>
    public Func<RequestContext<CallToolRequestParams>, CancellationToken, Task<CallToolResponse>>? CallToolHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for list prompts requests. This handler is invoked when a client requests 
    /// a list of available prompts from the server.
    /// </summary>
    /// <remarks>
    /// The handler should implement logic to list all prompts available on the server. Results from this handler
    /// are combined with any prompts defined in <see cref="PromptsCapability.PromptCollection"/>. 
    /// The handler supports pagination through the cursor parameter in <see cref="ListPromptsRequestParams"/>.
    /// When registered using <see cref="Configuration.McpServerBuilderExtensions.WithListPromptsHandler"/>, it enables 
    /// support for listing prompts dynamically.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.WithListPromptsHandler((request, cancellationToken) =>
    /// {
    ///     return Task.FromResult(new ListPromptsResult()
    ///     {
    ///         Prompts = [
    ///             new Prompt()
    ///             {
    ///                 Name = "simple_prompt", 
    ///                 Description = "A prompt without arguments"
    ///             },
    ///             new Prompt()
    ///             {
    ///                 Name = "complex_prompt",
    ///                 Description = "A prompt with arguments",
    ///                 Arguments = [
    ///                     new PromptArgument()
    ///                     {
    ///                         Name = "temperature",
    ///                         Description = "Temperature setting",
    ///                         Required = true
    ///                     }
    ///                 ]
    ///             }
    ///         ]
    ///     });
    /// });
    /// </code>
    /// </example>
    public Func<RequestContext<ListPromptsRequestParams>, CancellationToken, Task<ListPromptsResult>>? ListPromptsHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for get prompt requests. This handler is invoked when a client requests details
    /// for a specific prompt that isn't found in the <see cref="McpServerPrompt"/> collection.
    /// </summary>
    /// <remarks>
    /// The handler should implement logic to fetch or generate the requested prompt and return appropriate results.
    /// When registered using <see cref="McpServerBuilderExtensions.WithGetPromptHandler"/>, it enables support for dynamic prompt handling.
    /// </remarks>
    /// <example>
    /// <code>
    /// handlers.GetPromptHandler = async (request, cancellationToken) =>
    /// {
    ///     if (request.Params?.Name == "simple_prompt")
    ///     {
    ///         return new GetPromptResult
    ///         {
    ///             Messages = 
    ///             [
    ///                 new PromptMessage
    ///                 {
    ///                     Role = Role.User,
    ///                     Content = new Content
    ///                     {
    ///                         Type = "text",
    ///                         Text = "This is a simple prompt."
    ///                     }
    ///                 }
    ///             ]
    ///         };
    ///     }
    ///     else if (request.Params?.Name == "parameterized_prompt")
    ///     {
    ///         // Access arguments passed in the request
    ///         string style = request.Params.Arguments?["style"]?.ToString() ?? "default";
    ///         
    ///         return new GetPromptResult
    ///         {
    ///             Messages = 
    ///             [
    ///                 new PromptMessage
    ///                 {
    ///                     Role = Role.User,
    ///                     Content = new Content
    ///                     {
    ///                         Type = "text",
    ///                         Text = $"This is a prompt with style: {style}"
    ///                     }
    ///                 }
    ///             ]
    ///         };
    ///     }
    ///     
    ///     throw new McpException($"Unknown prompt: {request.Params?.Name}");
    /// };
    /// </code>
    /// </example>
    public Func<RequestContext<GetPromptRequestParams>, CancellationToken, Task<GetPromptResult>>? GetPromptHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for list resource templates requests.
    /// This handler processes requests to enumerate available resource templates that clients can use
    /// to create resources. Resource templates define the structure and URI templates for resources
    /// that can be created within the system.
    /// </summary>
    /// <example>
    /// <code>
    /// handlers.ListResourceTemplatesHandler = async (request, cancellationToken) =>
    /// {
    ///     return new ListResourceTemplatesResult
    ///     {
    ///         ResourceTemplates = 
    ///         [
    ///             new ResourceTemplate 
    ///             { 
    ///                 Name = "Document", 
    ///                 Description = "A document resource", 
    ///                 UriTemplate = "files://document/{id}" 
    ///             },
    ///             new ResourceTemplate 
    ///             {
    ///                 Name = "User Profile", 
    ///                 Description = "User profile information", 
    ///                 UriTemplate = "users://{userId}/profile" 
    ///             }
    ///         ]
    ///     };
    /// };
    /// </code>
    /// </example>
    public Func<RequestContext<ListResourceTemplatesRequestParams>, CancellationToken, Task<ListResourceTemplatesResult>>? ListResourceTemplatesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for list resources requests. This handler is invoked when a client
    /// requests a list of available resources matching specified criteria.
    /// </summary>
    /// <remarks>
    /// The handler should implement logic to enumerate resources based on the provided filter criteria
    /// and return matching resources. It supports pagination through the cursor mechanism.
    /// </remarks>
    /// <example>
    /// <code>
    /// handlers.ListResourcesHandler = async (request, cancellationToken) =>
    /// {
    ///     // Retrieve filter criteria from the request
    ///     var templateFilter = request.Params?.Template;
    ///     var cursor = request.Params?.Cursor;
    ///     var limit = request.Params?.Limit ?? 50;
    ///     
    ///     // Find matching resources (implementation details would vary)
    ///     var matchingResources = await _resourceRepository.FindAsync(
    ///         templateFilter, 
    ///         cursor, 
    ///         limit, 
    ///         cancellationToken);
    ///     
    ///     return new ListResourcesResult
    ///     {
    ///         Resources = matchingResources.Items.Select(r => new Resource
    ///         {
    ///             Uri = r.Uri,
    ///             Name = r.Name,
    ///             Description = r.Description,
    ///             // Include other properties as needed
    ///         }).ToList(),
    ///         NextCursor = matchingResources.HasMoreItems ? matchingResources.NextPageToken : null
    ///     };
    /// };
    /// </code>
    /// </example>
    public Func<RequestContext<ListResourcesRequestParams>, CancellationToken, Task<ListResourcesResult>>? ListResourcesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for read resources requests. This handler is invoked when a client
    /// requests the content of a specific resource identified by its URI.
    /// </summary>
    /// <remarks>
    /// The ReadResourceHandler is invoked when a client requests to read a specific resource via the MCP protocol.
    /// The handler should implement logic to locate and retrieve the requested resource.
    /// If the resource cannot be found, the handler should typically throw an appropriate exception.
    /// 
    /// The RequestContext parameter contains the client request information, including the resource URI in the Params property.
    /// The handler should return a ReadResourceResult containing the contents of the requested resource.
    /// </remarks>
    /// <example>
    /// <code>
    /// handlers.ReadResourceHandler = async (request, cancellationToken) =>
    /// {
    ///     var resourceUri = request.Params?.Uri;
    ///     if (string.IsNullOrEmpty(resourceUri))
    ///     {
    ///         throw new McpException("Resource URI is required");
    ///     }
    ///     
    ///     // Example pattern for handling different resource types by URI scheme
    ///     if (resourceUri.StartsWith("test://static/resource/"))
    ///     {
    ///         // Example: parse ID from URI and retrieve a resource
    ///         int id = int.Parse(resourceUri["test://static/resource/".Length..]);
    ///         
    ///         // Retrieve the resource content (implementation details would vary)
    ///         string content = $"This is static resource {id}";
    ///         
    ///         return new ReadResourceResult
    ///         {
    ///             Contents = 
    ///             [
    ///                 new TextResourceContents
    ///                 {
    ///                     Uri = resourceUri,
    ///                     MimeType = "text/plain",
    ///                     Text = content
    ///                 }
    ///             ]
    ///         };
    ///     }
    ///     
    ///     throw new McpException($"Resource not found: {resourceUri}");
    /// };
    /// </code>
    /// </example>
    public Func<RequestContext<ReadResourceRequestParams>, CancellationToken, Task<ReadResourceResult>>? ReadResourceHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for completion requests. This handler provides auto-completion suggestions
    /// for prompt arguments or resource references in the Model Context Protocol.
    /// </summary>
    /// <remarks>
    /// The handler processes auto-completion requests, returning a list of suggestions based on the 
    /// reference type and current argument value.
    /// </remarks>
    /// <example>
    /// <code>
    /// handlers.CompleteHandler = (ctx, ct) =>
    /// {
    ///     var completions = new Dictionary&lt;string, IEnumerable&lt;string&gt;&gt;
    ///     {
    ///         { "style", ["casual", "formal", "technical"] },
    ///         { "temperature", ["0", "0.7", "1.0"] }
    ///     };
    ///     
    ///     if (ctx.Params?.Ref.Type == "ref/prompt")
    ///     {
    ///         var arg = ctx.Params.Argument;
    ///         var values = completions.GetValueOrDefault(arg.Name, [])
    ///             .Where(v => v.StartsWith(arg.Value));
    ///             
    ///         return Task.FromResult(new CompleteResult
    ///         {
    ///             Completion = new Completion { 
    ///                 Values = [..values], 
    ///                 HasMore = false, 
    ///                 Total = values.Count() 
    ///             }
    ///         });
    ///     }
    ///     
    ///     return Task.FromResult(new CompleteResult());
    /// };
    /// </code>
    /// </example>
    public Func<RequestContext<CompleteRequestParams>, CancellationToken, Task<CompleteResult>>? CompleteHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for subscribe to resources messages. This handler is invoked when a client
    /// wants to receive notifications about changes to specific resources or resource patterns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handler should implement logic to register the client's interest in the specified resources
    /// and set up the necessary infrastructure to send notifications when those resources change.
    /// </para>
    /// <para>
    /// After a successful subscription, the server should send resource change notifications to the client
    /// whenever a relevant resource is created, updated, or deleted.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// handlers.SubscribeToResourcesHandler = async (request, cancellationToken) =>
    /// {
    ///     var patterns = request.Params?.ResourcePatterns;
    ///     if (patterns == null || !patterns.Any())
    ///     {
    ///         throw new McpException("Resource patterns are required for subscription");
    ///     }
    ///     
    ///     // Register subscription in your notification system
    ///     await _notificationManager.RegisterSubscriptionAsync(
    ///         request.Client.Id,
    ///         patterns,
    ///         async (changedResource) =>
    ///         {
    ///             // Send notifications when resources change
    ///             await request.Server.SendNotificationAsync(
    ///                 "resource/changed",
    ///                 new ResourceChangedNotification
    ///                 {
    ///                     Resource = changedResource,
    ///                     ChangeType = "updated"
    ///                 },
    ///                 cancellationToken);
    ///         },
    ///         cancellationToken);
    ///     
    ///     return new EmptyResult();
    /// };
    /// </code>
    /// </example>
    public Func<RequestContext<SubscribeRequestParams>, CancellationToken, Task<EmptyResult>>? SubscribeToResourcesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for unsubscribe from resources messages. This handler is invoked when a client
    /// wants to stop receiving notifications about previously subscribed resources.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handler should implement logic to remove the client's subscriptions to the specified resources
    /// and clean up any associated resources.
    /// </para>
    /// <para>
    /// After a successful unsubscription, the server should no longer send resource change notifications
    /// to the client for the specified resources.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// handlers.UnsubscribeFromResourcesHandler = async (request, cancellationToken) =>
    /// {
    ///     var subscriptionId = request.Params?.SubscriptionId;
    ///     if (string.IsNullOrEmpty(subscriptionId))
    ///     {
    ///         throw new McpException("Subscription ID is required for unsubscription");
    ///     }
    ///     
    ///     // Unregister subscription in your notification system
    ///     bool success = await _notificationManager.RemoveSubscriptionAsync(
    ///         request.Client.Id,
    ///         subscriptionId,
    ///         cancellationToken);
    ///         
    ///     if (!success)
    ///     {
    ///         throw new McpException($"Subscription not found: {subscriptionId}");
    ///     }
    ///     
    ///     return new EmptyResult();
    /// };
    /// </code>
    /// </example>
    public Func<RequestContext<UnsubscribeRequestParams>, CancellationToken, Task<EmptyResult>>? UnsubscribeFromResourcesHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for processing logging level change requests from clients.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This handler processes <c>logging/setLevel</c> requests from clients. When set, it enables
    /// clients to control which log messages they receive by specifying a minimum severity threshold.
    /// </para>
    /// <para>
    /// After handling a level change request, the server typically begins sending log messages
    /// at or above the specified level to the client as notifications/message notifications.
    /// </para>
    /// <para>
    /// Example implementation:
    /// <code>
    /// var _minimumLoggingLevel = LoggingLevel.Debug;
    /// 
    /// handler.SetLoggingLevelHandler = async (ctx, ct) =>
    /// {
    ///     if (ctx.Params?.Level is null)
    ///     {
    ///         throw new McpException("Missing required argument 'level'");
    ///     }
    ///     
    ///     _minimumLoggingLevel = ctx.Params.Level;
    ///     
    ///     await ctx.Server.SendNotificationAsync("notifications/message", new
    ///     {
    ///         Level = "debug",
    ///         Data = $"Logging level set to {_minimumLoggingLevel}",
    ///     });
    ///     
    ///     return new EmptyResult();
    /// });
    /// </code>
    /// </para>
    /// </remarks>
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
