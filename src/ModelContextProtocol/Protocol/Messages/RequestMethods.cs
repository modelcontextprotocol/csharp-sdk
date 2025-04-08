namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// Provides names for request methods used in the Model Context Protocol (MCP).
/// </summary>
public static class RequestMethods
{
    /// <summary>
    /// Sent from the client to request a list of tools the server has.
    /// </summary>
    public const string ToolsList = "tools/list";

    /// <summary>
    /// Used by the client to invoke a tool provided by the server.
    /// </summary>
    public const string ToolsCall = "tools/call";

    /// <summary>
    /// Sent from the client to request a list of prompts and prompt templates the server has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This request allows clients to retrieve a list of all prompts available on the server.
    /// The server combines prompts from its <see cref="Types.PromptsCapability.PromptCollection"/> and
    /// any prompts provided by a custom <see cref="Types.PromptsCapability.ListPromptsHandler"/>.
    /// </para>
    /// <para>
    /// The response supports pagination through a cursor-based mechanism, where the client can
    /// make subsequent requests with the cursor from the previous response to retrieve additional prompts.
    /// </para>
    /// <para>
    /// The client sends a <see cref="Types.ListPromptsRequestParams"/> object and receives a
    /// <see cref="Types.ListPromptsResult"/> containing available prompts in response.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// // Get all prompts available on the server
    /// var prompts = await client.ListPromptsAsync(cancellationToken);
    /// 
    /// // Display information about each prompt
    /// foreach (var prompt in prompts)
    /// {
    ///     Console.WriteLine($"Prompt: {prompt.Name}");
    ///     if (prompt.Description != null)
    ///     {
    ///         Console.WriteLine($"Description: {prompt.Description}");
    ///     }
    ///     if (prompt.Arguments?.Count > 0)
    ///     {
    ///         Console.WriteLine("Arguments:");
    ///         foreach (var arg in prompt.Arguments)
    ///         {
    ///             Console.WriteLine($"  - {arg.Name}: {arg.Description}");
    ///         }
    ///     }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public const string PromptsList = "prompts/list";

    /// <summary>
    /// Used by the client to get a prompt provided by the server.
    /// </summary>
    public const string PromptsGet = "prompts/get";

    /// <summary>
    /// Sent from the client to request a list of resources the server has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This request allows clients to retrieve a list of all resources available on the server.
    /// The server responds with a <see cref="Types.ListResourcesResult"/> containing available resources
    /// and pagination information.
    /// </para>
    /// <para>
    /// The request supports pagination through a cursor-based mechanism, where the client can
    /// make subsequent requests with the cursor from the previous response to retrieve additional resources.
    /// </para>
    /// <para>
    /// The client sends a <see cref="Types.ListResourcesRequestParams"/> object and receives a
    /// <see cref="Types.ListResourcesResult"/> containing available resources in response.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// // Simple request for resources (first page only)
    /// var result = await client.SendRequestAsync(
    ///     RequestMethods.ResourcesList,
    ///     new ListResourcesRequestParams(),
    ///     McpJsonUtilities.JsonContext.Default.ListResourcesRequestParams,
    ///     McpJsonUtilities.JsonContext.Default.ListResourcesResult);
    /// 
    /// // Get all resources with pagination (using the extension method)
    /// var allResources = await client.ListResourcesAsync();
    /// 
    /// // Display information about each resource
    /// foreach (var resource in allResources)
    /// {
    ///     Console.WriteLine($"Resource URI: {resource.Uri}");
    ///     Console.WriteLine($"Resource Type: {resource.Type}");
    ///     
    ///     // Access resource properties if available
    ///     if (resource.Properties?.Count > 0)
    ///     {
    ///         Console.WriteLine("Resource Properties:");
    ///         foreach (var prop in resource.Properties)
    ///         {
    ///             Console.WriteLine($"  - {prop.Key}: {prop.Value}");
    ///         }
    ///     }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public const string ResourcesList = "resources/list";

    /// <summary>
    /// Sent from the client to the server, to read a specific resource URI.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This request allows clients to retrieve the contents of a specific resource identified by its URI.
    /// The server processes the request by locating the resource and returning its contents, which can be
    /// either text-based or binary data.
    /// </para>
    /// <para>
    /// The client sends a <see cref="Types.ReadResourceRequestParams"/> object specifying the resource URI and
    /// receives a <see cref="Types.ReadResourceResult"/> containing the resource contents in response.
    /// </para>
    /// <para>
    /// Resources can be used to provide data for prompts, tools, or other server capabilities. They may
    /// represent files, database records, API responses, or any other data that the server can access.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Read a resource by its URI
    /// var result = await client.ReadResourceAsync("resource://documents/12345");
    /// 
    /// // Process text resource contents
    /// if (result.Contents.Count > 0 && result.Contents[0] is TextResourceContents textContent)
    /// {
    ///     Console.WriteLine($"Resource content: {textContent.Text}");
    ///     Console.WriteLine($"MIME type: {textContent.MimeType}");
    /// }
    /// 
    /// // Convert to AIContent for use with AI clients
    /// IList&lt;AIContent&gt; aiContents = result.Contents.ToAIContents();
    /// </code>
    /// </example>
    public const string ResourcesRead = "resources/read";

    /// <summary>
    /// Sent from the client to request a list of resource templates the server has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This request allows clients to retrieve a list of all resource templates available on the server.
    /// Resource templates define URI patterns, structure, and metadata for resources that can be accessed
    /// through the server.
    /// </para>
    /// <para>
    /// The server responds with a <see cref="Types.ListResourceTemplatesResult"/> containing available resource templates
    /// and pagination information. Each template in the response provides metadata such as URI template patterns,
    /// name, description, and MIME type information.
    /// </para>
    /// <para>
    /// The request supports pagination through a cursor-based mechanism, where the client can
    /// make subsequent requests with the cursor from the previous response to retrieve additional templates.
    /// </para>
    /// <para>
    /// The client sends a <see cref="Types.ListResourceTemplatesRequestParams"/> object and receives a
    /// <see cref="Types.ListResourceTemplatesResult"/> containing available resource templates in response.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// // Simple request for resource templates (first page only)
    /// var result = await client.SendRequestAsync(
    ///     RequestMethods.ResourcesTemplatesList,
    ///     new ListResourceTemplatesRequestParams(),
    ///     McpJsonUtilities.JsonContext.Default.ListResourceTemplatesRequestParams,
    ///     McpJsonUtilities.JsonContext.Default.ListResourceTemplatesResult);
    /// 
    /// // Get all resource templates with pagination (using the extension method)
    /// var allTemplates = await client.ListResourceTemplatesAsync();
    /// 
    /// // Display information about each resource template
    /// foreach (var template in allTemplates)
    /// {
    ///     Console.WriteLine($"Template Name: {template.Name}");
    ///     Console.WriteLine($"URI Template: {template.UriTemplate}");
    ///     
    ///     if (template.Description != null)
    ///     {
    ///         Console.WriteLine($"Description: {template.Description}");
    ///     }
    ///     
    ///     if (template.MimeType != null)
    ///     {
    ///         Console.WriteLine($"MIME Type: {template.MimeType}");
    ///     }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public const string ResourcesTemplatesList = "resources/templates/list";

    /// <summary>
    /// Sent from the client to request resources/updated notifications from the server whenever a particular resource changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This request allows clients to subscribe to specific resources identified by their URIs. When a subscribed 
    /// resource changes, the server sends a notification to the client with updated resource information, enabling 
    /// real-time updates without polling.
    /// </para>
    /// <para>
    /// The client sends a <see cref="Types.SubscribeRequestParams"/> object specifying the resource URI and
    /// receives an <see cref="Types.EmptyResult"/> in response. After a successful subscription, the server will 
    /// send <see cref="NotificationMethods.ResourceUpdatedNotification"/> notifications with 
    /// <see cref="Types.ResourceUpdatedNotificationParams"/> whenever the resource changes.
    /// </para>
    /// <para>
    /// Subscriptions remain active until explicitly canceled using <see cref="ResourcesUnsubscribe"/> 
    /// or until the connection is terminated.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Subscribe to changes on a specific resource
    /// await client.SubscribeToResourceAsync("resource://documents/123", cancellationToken);
    /// 
    /// // Handle resource change notifications in your client implementation
    /// client.ResourceChanged += (sender, e) => 
    /// {
    ///     Console.WriteLine($"Resource {e.Uri} has changed");
    ///     // Update your application state with the new resource information
    /// };
    /// 
    /// // Later, unsubscribe when no longer needed
    /// await client.UnsubscribeFromResourceAsync("resource://documents/123", cancellationToken);
    /// </code>
    /// </example>
    public const string ResourcesSubscribe = "resources/subscribe";

    /// <summary>
    /// Sent from the client to request cancellation of resources/updated notifications from the server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This request allows clients to unsubscribe from specific resources they previously subscribed to.
    /// After a successful unsubscribe request, the server will stop sending notifications about changes
    /// to the specified resource.
    /// </para>
    /// <para>
    /// The client sends a <see cref="Types.UnsubscribeFromResourceRequestParams"/> object specifying the resource URI and
    /// receives an <see cref="Types.EmptyResult"/> in response. After successful processing, the server will 
    /// immediately stop sending <see cref="NotificationMethods.ResourceUpdatedNotification"/> notifications
    /// for the specified resource.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Unsubscribe from a specific resource
    /// await client.UnsubscribeFromResourceAsync("resource://documents/123", cancellationToken);
    /// </code>
    /// </example>
    /// <seealso cref="ResourcesSubscribe"/>
    /// <seealso cref="Types.UnsubscribeFromResourceRequestParams"/>
    /// <seealso cref="Types.SubscribeRequestParams"/>
    public const string ResourcesUnsubscribe = "resources/unsubscribe";

    /// <summary>
    /// Sent from the server to request a list of root URIs from the client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This request allows servers to discover what root URIs are available on the client.
    /// Root URIs serve as entry points for resource navigation in the Model Context Protocol.
    /// </para>
    /// <para>
    /// The server sends a <see cref="Types.ListRootsRequestParams"/> object and receives a
    /// <see cref="Types.ListRootsResult"/> containing available roots in response. Each root in the
    /// response includes a URI and optional metadata like a human-readable name.
    /// </para>
    /// <para>
    /// This request is only valid when the client has the <see cref="Types.RootsCapability"/> set
    /// in its <see cref="Types.ClientCapabilities"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Server requesting roots from a client
    /// var result = await server.RequestRootsAsync(
    ///     new ListRootsRequestParams(),
    ///     CancellationToken.None);
    ///     
    /// // Access the roots returned by the client
    /// foreach (var root in result.Roots)
    /// {
    ///     Console.WriteLine($"Root URI: {root.Uri}");
    ///     if (root.Name != null)
    ///     {
    ///         Console.WriteLine($"Root Name: {root.Name}");
    ///     }
    /// }
    /// </code>
    /// </example>
    /// <seealso cref="Types.ListRootsRequestParams"/>
    /// <seealso cref="Types.ListRootsResult"/>
    /// <seealso cref="Types.Root"/>
    /// <seealso cref="Types.RootsCapability"/>
    /// <seealso cref="Server.McpServerExtensions.RequestRootsAsync"/>
    public const string RootsList = "roots/list";

    /// <summary>
    /// A ping, issued by either the server or the client, to check that the other party is still alive.
    /// </summary>
    public const string Ping = "ping";

    /// <summary>
    /// A request from the client to the server to enable or adjust logging level.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This request allows clients to control which log messages they receive from the server
    /// by setting a minimum severity threshold. After processing this request, the server will
    /// send log messages with severity at or above the specified level to the client as
    /// notifications/message notifications.
    /// </para>
    /// <para>
    /// The client sends a <see cref="Types.SetLevelRequestParams"/> object specifying the desired
    /// <see cref="Types.LoggingLevel"/> and receives an <see cref="Types.EmptyResult"/> in response.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// // Request the server to send Warning level and higher severity logs
    /// await client.SetLoggingLevel(LoggingLevel.Warning);
    /// </code>
    /// </para>
    /// </remarks>
    public const string LoggingSetLevel = "logging/setLevel";

    /// <summary>
    /// A request from the client to the server, to ask for completion suggestions.
    /// Used to provide autocompletion-like functionality for arguments in a resource reference or a prompt template.
    /// The client provides a reference (resource or prompt), argument name, and partial value, and the server 
    /// responds with matching completion options.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The server's response is a <see cref="Types.CompleteResult"/> containing suggested values.
    /// </para>
    /// <para>
    /// Example usage with resource reference:
    /// <code>
    /// var result = await client.CompleteAsync(
    ///     new Reference { 
    ///         Type = "ref/resource", 
    ///         Uri = "resource://example/1"
    ///     },
    ///     argumentName: "parameter_name",
    ///     argumentValue: "par"
    /// );
    /// </code>
    /// </para>
    /// <para>
    /// Example usage with prompt reference:
    /// <code>
    /// var result = await client.CompleteAsync(
    ///     new Reference { 
    ///         Type = "ref/prompt", 
    ///         Name = "my_prompt"
    ///     },
    ///     argumentName: "style",
    ///     argumentValue: "fo"
    /// );
    /// </code>
    /// </para>
    /// </remarks>
    public const string CompletionComplete = "completion/complete";

    /// <summary>
    /// A request from the server to sample an LLM via the client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This request allows servers to utilize a language model (LLM) available on the client side to generate text or image responses
    /// based on provided messages. It is part of the sampling capability in the Model Context Protocol and enables servers to access
    /// client-side AI models without needing direct API access to those models.
    /// </para>
    /// <para>
    /// The server sends a <see cref="Types.CreateMessageRequestParams"/> object specifying the conversation messages and generation
    /// parameters, and receives a <see cref="Types.CreateMessageResult"/> containing the generated content in response.
    /// </para>
    /// <para>
    /// This request is only valid when the client has the <see cref="Types.SamplingCapability"/> set in its <see cref="Types.ClientCapabilities"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Create sampling parameters
    /// var samplingParams = new CreateMessageRequestParams
    /// {
    ///     Messages = [
    ///         new SamplingMessage
    ///         {
    ///             Role = Role.User,
    ///             Content = new Content { Type = "text", Text = "What is the capital of France?" }
    ///         }
    ///     ],
    ///     MaxTokens = 100,
    ///     Temperature = 0.7f
    /// };
    /// 
    /// // Request sampling from the client
    /// var result = await server.RequestSamplingAsync(samplingParams, CancellationToken.None);
    /// Console.WriteLine(result.Content.Text);
    /// </code>
    /// </example>
    /// <seealso cref="Types.CreateMessageRequestParams"/>
    /// <seealso cref="Types.CreateMessageResult"/>
    /// <seealso cref="Types.SamplingCapability"/>
    /// <seealso cref="Server.McpServerExtensions.RequestSamplingAsync"/>
    public const string SamplingCreateMessage = "sampling/createMessage";

    /// <summary>
    /// This request is sent from the client to the server when it first connects, asking it to begin initialization.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The initialize request is the first request sent by the client to the server. It provides client information
    /// and capabilities to the server during connection establishment. The server responds with its own capabilities
    /// and information, establishing the protocol version and available features for the session.
    /// </para>
    /// <para>
    /// The client sends an <see cref="Types.InitializeRequestParams"/> object and expects an 
    /// <see cref="Types.InitializeResult"/> in response. After receiving the response, the client should 
    /// send an <see cref="NotificationMethods.InitializedNotification"/> to signal completion of initialization.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// var response = await client.SendRequestAsync(
    ///     new JsonRpcRequest {
    ///         Method = RequestMethods.Initialize,
    ///         Id = new RequestId(1),
    ///         Params = JsonSerializer.SerializeToNode(new InitializeRequestParams {
    ///             ProtocolVersion = "2024-11-05",
    ///             ClientInfo = new Implementation { Name = "MyMcpClient", Version = "1.0.0" },
    ///             Capabilities = new ClientCapabilities()
    ///         })
    ///     },
    ///     cancellationToken
    /// );
    /// </code>
    /// </para>
    /// </remarks>
    public const string Initialize = "initialize";
}