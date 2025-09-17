using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ModelContextProtocol.Client;

/// <summary>
/// Provides extension methods for interacting with an <see cref="McpClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// This class contains extension methods that simplify common operations with an MCP client,
/// such as pinging a server, listing and working with tools, prompts, and resources, and
/// managing subscriptions to resources.
/// </para>
/// </remarks>
public static class McpClientExtensions
{
    /// <summary>
    /// Creates a sampling handler for use with <see cref="SamplingCapability.SamplingHandler"/> that will
    /// satisfy sampling requests using the specified <see cref="IChatClient"/>.
    /// </summary>
    /// <param name="chatClient">The <see cref="IChatClient"/> with which to satisfy sampling requests.</param>
    /// <returns>The created handler delegate that can be assigned to <see cref="SamplingCapability.SamplingHandler"/>.</returns>
    /// <remarks>
    /// <para>
    /// This method creates a function that converts MCP message requests into chat client calls, enabling
    /// an MCP client to generate text or other content using an actual AI model via the provided chat client.
    /// </para>
    /// <para>
    /// The handler can process text messages, image messages, and resource messages as defined in the
    /// Model Context Protocol.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="chatClient"/> is <see langword="null"/>.</exception>
    public static Func<CreateMessageRequestParams?, IProgress<ProgressNotificationValue>, CancellationToken, ValueTask<CreateMessageResult>> CreateSamplingHandler(
        this IChatClient chatClient)
    {
        Throw.IfNull(chatClient);

        return async (requestParams, progress, cancellationToken) =>
        {
            Throw.IfNull(requestParams);

            var (messages, options) = requestParams.ToChatClientArguments();
            var progressToken = requestParams.ProgressToken;

            List<ChatResponseUpdate> updates = [];
            await foreach (var update in chatClient.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                updates.Add(update);

                if (progressToken is not null)
                {
                    progress.Report(new()
                    {
                        Progress = updates.Count,
                    });
                }
            }

            return updates.ToChatResponse().ToCreateMessageResult();
        };
    }

    /// <summary>
    /// Sends a ping request to verify server connectivity.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that completes when the ping is successful.</returns>
    /// <remarks>
    /// <para>
    /// This method is used to check if the MCP server is online and responding to requests.
    /// It can be useful for health checking, ensuring the connection is established, or verifying 
    /// that the client has proper authorization to communicate with the server.
    /// </para>
    /// <para>
    /// The ping operation is lightweight and does not require any parameters. A successful completion
    /// of the task indicates that the server is operational and accessible.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">Thrown when the server cannot be reached or returns an error response.</exception>
    [Obsolete($"Use {nameof(McpClient)}.{nameof(McpClient.PingAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static Task PingAsync(this IMcpClient client, CancellationToken cancellationToken = default)
        => AsClientOrThrow(client).PingAsync(cancellationToken);

    /// <summary>
    /// Retrieves a list of available tools from the server.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="serializerOptions">The serializer options governing tool parameter serialization. If null, the default options will be used.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A list of all available tools as <see cref="McpClientTool"/> instances.</returns>
    /// <remarks>
    /// <para>
    /// This method fetches all available tools from the MCP server and returns them as a complete list.
    /// It automatically handles pagination with cursors if the server responds with only a portion per request.
    /// </para>
    /// <para>
    /// For servers with a large number of tools and that responds with paginated responses, consider using 
    /// <see cref="EnumerateToolsAsync"/> instead, as it streams tools as they arrive rather than loading them all at once.
    /// </para>
    /// <para>
    /// The serializer options provided are flowed to each <see cref="McpClientTool"/> and will be used
    /// when invoking tools in order to serialize any parameters.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Get all tools available on the server
    /// var tools = await mcpClient.ListToolsAsync();
    /// 
    /// // Use tools with an AI client
    /// ChatOptions chatOptions = new()
    /// {
    ///     Tools = [.. tools]
    /// };
    /// 
    /// await foreach (var update in chatClient.GetStreamingResponseAsync(userMessage, chatOptions))
    /// {
    ///     Console.Write(update);
    /// }
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    [Obsolete($"Use {nameof(McpClient)}.{nameof(McpClient.ListToolsAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static ValueTask<IList<McpClientTool>> ListToolsAsync(
        this IMcpClient client,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
        => AsClientOrThrow(client).ListToolsAsync(serializerOptions, cancellationToken);

    /// <summary>
    /// Creates an enumerable for asynchronously enumerating all available tools from the server.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="serializerOptions">The serializer options governing tool parameter serialization. If null, the default options will be used.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>An asynchronous sequence of all available tools as <see cref="McpClientTool"/> instances.</returns>
    /// <remarks>
    /// <para>
    /// This method uses asynchronous enumeration to retrieve tools from the server, which allows processing tools
    /// as they arrive rather than waiting for all tools to be retrieved. The method automatically handles pagination
    /// with cursors if the server responds with tools split across multiple responses.
    /// </para>
    /// <para>
    /// The serializer options provided are flowed to each <see cref="McpClientTool"/> and will be used
    /// when invoking tools in order to serialize any parameters.
    /// </para>
    /// <para>
    /// Every iteration through the returned <see cref="IAsyncEnumerable{McpClientTool}"/>
    /// will result in re-querying the server and yielding the sequence of available tools.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Enumerate all tools available on the server
    /// await foreach (var tool in client.EnumerateToolsAsync())
    /// {
    ///     Console.WriteLine($"Tool: {tool.Name}");
    /// }
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    [Obsolete($"Use {nameof(McpClient)}.{nameof(McpClient.EnumerateToolsAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static IAsyncEnumerable<McpClientTool> EnumerateToolsAsync(
        this IMcpClient client,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
        => AsClientOrThrow(client).EnumerateToolsAsync(serializerOptions, cancellationToken);

    /// <summary>
    /// Retrieves a list of available prompts from the server.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A list of all available prompts as <see cref="McpClientPrompt"/> instances.</returns>
    /// <remarks>
    /// <para>
    /// This method fetches all available prompts from the MCP server and returns them as a complete list.
    /// It automatically handles pagination with cursors if the server responds with only a portion per request.
    /// </para>
    /// <para>
    /// For servers with a large number of prompts and that responds with paginated responses, consider using 
    /// <see cref="EnumeratePromptsAsync"/> instead, as it streams prompts as they arrive rather than loading them all at once.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    [Obsolete($"Use {nameof(McpClient)}.{nameof(McpClient.ListPromptsAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static ValueTask<IList<McpClientPrompt>> ListPromptsAsync(
        this IMcpClient client, CancellationToken cancellationToken = default)
        => AsClientOrThrow(client).ListPromptsAsync(cancellationToken);

    /// <summary>
    /// Creates an enumerable for asynchronously enumerating all available prompts from the server.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>An asynchronous sequence of all available prompts as <see cref="McpClientPrompt"/> instances.</returns>
    /// <remarks>
    /// <para>
    /// This method uses asynchronous enumeration to retrieve prompts from the server, which allows processing prompts
    /// as they arrive rather than waiting for all prompts to be retrieved. The method automatically handles pagination
    /// with cursors if the server responds with prompts split across multiple responses.
    /// </para>
    /// <para>
    /// Every iteration through the returned <see cref="IAsyncEnumerable{McpClientPrompt}"/>
    /// will result in re-querying the server and yielding the sequence of available prompts.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Enumerate all prompts available on the server
    /// await foreach (var prompt in client.EnumeratePromptsAsync())
    /// {
    ///     Console.WriteLine($"Prompt: {prompt.Name}");
    /// }
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    [Obsolete($"Use {nameof(McpClient)}.{nameof(McpClient.EnumeratePromptsAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static IAsyncEnumerable<McpClientPrompt> EnumeratePromptsAsync(
        this IMcpClient client, CancellationToken cancellationToken = default)
        => AsClientOrThrow(client).EnumeratePromptsAsync(cancellationToken);

    /// <summary>
    /// Retrieves a specific prompt from the MCP server.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="name">The name of the prompt to retrieve.</param>
    /// <param name="arguments">Optional arguments for the prompt. Keys are parameter names, and values are the argument values.</param>
    /// <param name="serializerOptions">The serialization options governing argument serialization.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task containing the prompt's result with content and messages.</returns>
    /// <remarks>
    /// <para>
    /// This method sends a request to the MCP server to create the specified prompt with the provided arguments.
    /// The server will process the arguments and return a prompt containing messages or other content.
    /// </para>
    /// <para>
    /// Arguments are serialized into JSON and passed to the server, where they may be used to customize the 
    /// prompt's behavior or content. Each prompt may have different argument requirements.
    /// </para>
    /// <para>
    /// The returned <see cref="GetPromptResult"/> contains a collection of <see cref="PromptMessage"/> objects,
    /// which can be converted to <see cref="ChatMessage"/> objects using the <see cref="AIContentExtensions.ToChatMessages"/> method.
    /// </para>
    /// </remarks>
    /// <exception cref="McpException">Thrown when the prompt does not exist, when required arguments are missing, or when the server encounters an error processing the prompt.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    [Obsolete($"Use {nameof(McpClient)}.{nameof(McpClient.GetPromptAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static ValueTask<GetPromptResult> GetPromptAsync(
        this IMcpClient client,
        string name,
        IReadOnlyDictionary<string, object?>? arguments = null,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
        => AsClientOrThrow(client).GetPromptAsync(name, arguments, serializerOptions, cancellationToken);

    /// <summary>
    /// Retrieves a list of available resource templates from the server.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A list of all available resource templates as <see cref="ResourceTemplate"/> instances.</returns>
    /// <remarks>
    /// <para>
    /// This method fetches all available resource templates from the MCP server and returns them as a complete list.
    /// It automatically handles pagination with cursors if the server responds with only a portion per request.
    /// </para>
    /// <para>
    /// For servers with a large number of resource templates and that responds with paginated responses, consider using 
    /// <see cref="EnumerateResourceTemplatesAsync"/> instead, as it streams templates as they arrive rather than loading them all at once.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    [Obsolete($"Use {nameof(McpClient)}.{nameof(McpClient.ListResourceTemplatesAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static ValueTask<IList<McpClientResourceTemplate>> ListResourceTemplatesAsync(
        this IMcpClient client, CancellationToken cancellationToken = default)
        => AsClientOrThrow(client).ListResourceTemplatesAsync(cancellationToken);

    /// <summary>
    /// Creates an enumerable for asynchronously enumerating all available resource templates from the server.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>An asynchronous sequence of all available resource templates as <see cref="ResourceTemplate"/> instances.</returns>
    /// <remarks>
    /// <para>
    /// This method uses asynchronous enumeration to retrieve resource templates from the server, which allows processing templates
    /// as they arrive rather than waiting for all templates to be retrieved. The method automatically handles pagination
    /// with cursors if the server responds with templates split across multiple responses.
    /// </para>
    /// <para>
    /// Every iteration through the returned <see cref="IAsyncEnumerable{McpClientResourceTemplate}"/>
    /// will result in re-querying the server and yielding the sequence of available resource templates.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Enumerate all resource templates available on the server
    /// await foreach (var template in client.EnumerateResourceTemplatesAsync())
    /// {
    ///     Console.WriteLine($"Template: {template.Name}");
    /// }
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    [Obsolete($"Use {nameof(McpClient)}.{nameof(McpClient.EnumerateResourceTemplatesAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static IAsyncEnumerable<McpClientResourceTemplate> EnumerateResourceTemplatesAsync(
        this IMcpClient client, CancellationToken cancellationToken = default)
        => AsClientOrThrow(client).EnumerateResourceTemplatesAsync(cancellationToken);

    /// <summary>
    /// Retrieves a list of available resources from the server.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A list of all available resources as <see cref="Resource"/> instances.</returns>
    /// <remarks>
    /// <para>
    /// This method fetches all available resources from the MCP server and returns them as a complete list.
    /// It automatically handles pagination with cursors if the server responds with only a portion per request.
    /// </para>
    /// <para>
    /// For servers with a large number of resources and that responds with paginated responses, consider using 
    /// <see cref="EnumerateResourcesAsync"/> instead, as it streams resources as they arrive rather than loading them all at once.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Get all resources available on the server
    /// var resources = await client.ListResourcesAsync();
    /// 
    /// // Display information about each resource
    /// foreach (var resource in resources)
    /// {
    ///     Console.WriteLine($"Resource URI: {resource.Uri}");
    /// }
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    [Obsolete($"Use {nameof(McpClient)}.{nameof(McpClient.ListResourcesAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static ValueTask<IList<McpClientResource>> ListResourcesAsync(
        this IMcpClient client, CancellationToken cancellationToken = default)
        => AsClientOrThrow(client).ListResourcesAsync(cancellationToken);

    /// <summary>
    /// Creates an enumerable for asynchronously enumerating all available resources from the server.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>An asynchronous sequence of all available resources as <see cref="Resource"/> instances.</returns>
    /// <remarks>
    /// <para>
    /// This method uses asynchronous enumeration to retrieve resources from the server, which allows processing resources
    /// as they arrive rather than waiting for all resources to be retrieved. The method automatically handles pagination
    /// with cursors if the server responds with resources split across multiple responses.
    /// </para>
    /// <para>
    /// Every iteration through the returned <see cref="IAsyncEnumerable{McpClientResource}"/>
    /// will result in re-querying the server and yielding the sequence of available resources.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Enumerate all resources available on the server
    /// await foreach (var resource in client.EnumerateResourcesAsync())
    /// {
    ///     Console.WriteLine($"Resource URI: {resource.Uri}");
    /// }
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    [Obsolete($"Use {nameof(McpClient)}.{nameof(McpClient.EnumerateResourcesAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static IAsyncEnumerable<McpClientResource> EnumerateResourcesAsync(
        this IMcpClient client, CancellationToken cancellationToken = default)
        => AsClientOrThrow(client).EnumerateResourcesAsync(cancellationToken);

    /// <summary>
    /// Reads a resource from the server.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="uri">The uri of the resource.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="uri"/> is empty or composed entirely of whitespace.</exception>
    [Obsolete($"Use {nameof(McpClient)}.{nameof(McpClient.ReadResourceAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static ValueTask<ReadResourceResult> ReadResourceAsync(
        this IMcpClient client, string uri, CancellationToken cancellationToken = default)
        => AsClientOrThrow(client).ReadResourceAsync(uri, cancellationToken);

    /// <summary>
    /// Reads a resource from the server.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="uri">The uri of the resource.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    [Obsolete($"Use {nameof(McpClient)}.{nameof(McpClient.ReadResourceAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static ValueTask<ReadResourceResult> ReadResourceAsync(
        this IMcpClient client, Uri uri, CancellationToken cancellationToken = default)
        => AsClientOrThrow(client).ReadResourceAsync(uri, cancellationToken);

    /// <summary>
    /// Reads a resource from the server.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="uriTemplate">The uri template of the resource.</param>
    /// <param name="arguments">Arguments to use to format <paramref name="uriTemplate"/>.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="uriTemplate"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="uriTemplate"/> is empty or composed entirely of whitespace.</exception>
    [Obsolete($"Use {nameof(McpClient)}.{nameof(McpClient.ReadResourceAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static ValueTask<ReadResourceResult> ReadResourceAsync(
        this IMcpClient client, string uriTemplate, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken = default)
        => AsClientOrThrow(client).ReadResourceAsync(uriTemplate, arguments, cancellationToken);

    /// <summary>
    /// Requests completion suggestions for a prompt argument or resource reference.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="reference">The reference object specifying the type and optional URI or name.</param>
    /// <param name="argumentName">The name of the argument for which completions are requested.</param>
    /// <param name="argumentValue">The current value of the argument, used to filter relevant completions.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="CompleteResult"/> containing completion suggestions.</returns>
    /// <remarks>
    /// <para>
    /// This method allows clients to request auto-completion suggestions for arguments in a prompt template
    /// or for resource references.
    /// </para>
    /// <para>
    /// When working with prompt references, the server will return suggestions for the specified argument
    /// that match or begin with the current argument value. This is useful for implementing intelligent
    /// auto-completion in user interfaces.
    /// </para>
    /// <para>
    /// When working with resource references, the server will return suggestions relevant to the specified 
    /// resource URI.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="reference"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="argumentName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="argumentName"/> is empty or composed entirely of whitespace.</exception>
    /// <exception cref="McpException">The server returned an error response.</exception>
    [Obsolete($"Use {nameof(McpClient)}.{nameof(McpClient.CompleteAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static ValueTask<CompleteResult> CompleteAsync(this IMcpClient client, Reference reference, string argumentName, string argumentValue, CancellationToken cancellationToken = default)
        => AsClientOrThrow(client).CompleteAsync(reference, argumentName, argumentValue, cancellationToken);

    /// <summary>
    /// Subscribes to a resource on the server to receive notifications when it changes.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="uri">The URI of the resource to which to subscribe.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// This method allows the client to register interest in a specific resource identified by its URI.
    /// When the resource changes, the server will send notifications to the client, enabling real-time
    /// updates without polling.
    /// </para>
    /// <para>
    /// The subscription remains active until explicitly unsubscribed using <see cref="M:UnsubscribeFromResourceAsync"/>
    /// or until the client disconnects from the server.
    /// </para>
    /// <para>
    /// To handle resource change notifications, register an event handler for the appropriate notification events,
    /// such as with <see cref="IMcpEndpoint.RegisterNotificationHandler"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="uri"/> is empty or composed entirely of whitespace.</exception>
    [Obsolete($"Use {nameof(McpClient)}.{nameof(McpClient.SubscribeToResourceAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static Task SubscribeToResourceAsync(this IMcpClient client, string uri, CancellationToken cancellationToken = default)
        => AsClientOrThrow(client).SubscribeToResourceAsync(uri, cancellationToken);

    /// <summary>
    /// Subscribes to a resource on the server to receive notifications when it changes.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="uri">The URI of the resource to which to subscribe.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// This method allows the client to register interest in a specific resource identified by its URI.
    /// When the resource changes, the server will send notifications to the client, enabling real-time
    /// updates without polling.
    /// </para>
    /// <para>
    /// The subscription remains active until explicitly unsubscribed using <see cref="M:UnsubscribeFromResourceAsync"/>
    /// or until the client disconnects from the server.
    /// </para>
    /// <para>
    /// To handle resource change notifications, register an event handler for the appropriate notification events,
    /// such as with <see cref="IMcpEndpoint.RegisterNotificationHandler"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    [Obsolete($"Use {nameof(McpClient)}.{nameof(McpClient.SubscribeToResourceAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static Task SubscribeToResourceAsync(this IMcpClient client, Uri uri, CancellationToken cancellationToken = default)
        => AsClientOrThrow(client).SubscribeToResourceAsync(uri, cancellationToken);

    /// <summary>
    /// Unsubscribes from a resource on the server to stop receiving notifications about its changes.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="uri">The URI of the resource to unsubscribe from.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// This method cancels a previous subscription to a resource, stopping the client from receiving
    /// notifications when that resource changes.
    /// </para>
    /// <para>
    /// The unsubscribe operation is idempotent, meaning it can be called multiple times for the same
    /// resource without causing errors, even if there is no active subscription.
    /// </para>
    /// <para>
    /// Due to the nature of the MCP protocol, it is possible the client may receive notifications after
    /// unsubscribing if those notifications were issued by the server prior to the unsubscribe request being received.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="uri"/> is empty or composed entirely of whitespace.</exception>
    [Obsolete($"Use {nameof(McpClient)}.{nameof(McpClient.UnsubscribeFromResourceAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static Task UnsubscribeFromResourceAsync(this IMcpClient client, string uri, CancellationToken cancellationToken = default)
        => AsClientOrThrow(client).UnsubscribeFromResourceAsync(uri, cancellationToken);

    /// <summary>
    /// Unsubscribes from a resource on the server to stop receiving notifications about its changes.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="uri">The URI of the resource to unsubscribe from.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// This method cancels a previous subscription to a resource, stopping the client from receiving
    /// notifications when that resource changes.
    /// </para>
    /// <para>
    /// The unsubscribe operation is idempotent, meaning it can be called multiple times for the same
    /// resource without causing errors, even if there is no active subscription.
    /// </para>
    /// <para>
    /// Due to the nature of the MCP protocol, it is possible the client may receive notifications after
    /// unsubscribing if those notifications were issued by the server prior to the unsubscribe request being received.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    [Obsolete($"Use {nameof(McpClient)}.{nameof(McpClient.UnsubscribeFromResourceAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static Task UnsubscribeFromResourceAsync(this IMcpClient client, Uri uri, CancellationToken cancellationToken = default)
        => AsClientOrThrow(client).UnsubscribeFromResourceAsync(uri, cancellationToken);

    /// <summary>
    /// Invokes a tool on the server.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="toolName">The name of the tool to call on the server..</param>
    /// <param name="arguments">An optional dictionary of arguments to pass to the tool. Each key represents a parameter name,
    /// and its associated value represents the argument value.
    /// </param>
    /// <param name="progress">
    /// An optional <see cref="IProgress{T}"/> to have progress notifications reported to it. Setting this to a non-<see langword="null"/>
    /// value will result in a progress token being included in the call, and any resulting progress notifications during the operation
    /// routed to this instance.
    /// </param>
    /// <param name="serializerOptions">
    /// The JSON serialization options governing argument serialization. If <see langword="null"/>, the default serialization options will be used.
    /// </param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>
    /// A task containing the <see cref="CallToolResult"/> from the tool execution. The response includes
    /// the tool's output content, which may be structured data, text, or an error message.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="toolName"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The server could not find the requested tool, or the server encountered an error while processing the request.</exception>
    /// <example>
    /// <code>
    /// // Call a simple echo tool with a string argument
    /// var result = await client.CallToolAsync(
    ///     "echo",
    ///     new Dictionary&lt;string, object?&gt;
    ///     {
    ///         ["message"] = "Hello MCP!"
    ///     });
    /// </code>
    /// </example>
    [Obsolete($"Use {nameof(McpClient)}.{nameof(McpClient.CallToolAsync)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    public static ValueTask<CallToolResult> CallToolAsync(
        this IMcpClient client,
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        IProgress<ProgressNotificationValue>? progress = null,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
        => AsClientOrThrow(client).CallToolAsync(toolName, arguments, progress, serializerOptions, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#pragma warning disable CS0618 // Type or member is obsolete
    private static McpClient AsClientOrThrow(IMcpClient client, [CallerMemberName] string memberName = "")
#pragma warning restore CS0618 // Type or member is obsolete
    {
        if (client is not McpClient mcpClient)
        {
            ThrowInvalidEndpointType(memberName);
        }

        return mcpClient;

        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ThrowInvalidEndpointType(string memberName)
            => throw new InvalidOperationException(
                $"Only arguments assignable to '{nameof(McpClient)}' are supported. " +
                $"Prefer using '{nameof(McpClient)}.{memberName}' instead, as " +
                $"'{nameof(McpClientExtensions)}.{memberName}' is obsolete and will be " +
                $"removed in the future.");
    }

    /// <summary>
    /// Converts the contents of a <see cref="CreateMessageRequestParams"/> into a pair of
    /// <see cref="IEnumerable{ChatMessage}"/> and <see cref="ChatOptions"/> instances to use
    /// as inputs into a <see cref="IChatClient"/> operation.
    /// </summary>
    /// <param name="requestParams"></param>
    /// <returns>The created pair of messages and options.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    internal static (IList<ChatMessage> Messages, ChatOptions? Options) ToChatClientArguments(
        this CreateMessageRequestParams requestParams)
    {
        Throw.IfNull(requestParams);

        ChatOptions? options = null;

        if (requestParams.MaxTokens is int maxTokens)
        {
            (options ??= new()).MaxOutputTokens = maxTokens;
        }

        if (requestParams.Temperature is float temperature)
        {
            (options ??= new()).Temperature = temperature;
        }

        if (requestParams.StopSequences is { } stopSequences)
        {
            (options ??= new()).StopSequences = stopSequences.ToArray();
        }

        List<ChatMessage> messages =
            (from sm in requestParams.Messages
             let aiContent = sm.Content.ToAIContent()
             where aiContent is not null
             select new ChatMessage(sm.Role == Role.Assistant ? ChatRole.Assistant : ChatRole.User, [aiContent]))
            .ToList();

        return (messages, options);
    }

    /// <summary>Converts the contents of a <see cref="ChatResponse"/> into a <see cref="CreateMessageResult"/>.</summary>
    /// <param name="chatResponse">The <see cref="ChatResponse"/> whose contents should be extracted.</param>
    /// <returns>The created <see cref="CreateMessageResult"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="chatResponse"/> is <see langword="null"/>.</exception>
    internal static CreateMessageResult ToCreateMessageResult(this ChatResponse chatResponse)
    {
        Throw.IfNull(chatResponse);

        // The ChatResponse can include multiple messages, of varying modalities, but CreateMessageResult supports
        // only either a single blob of text or a single image. Heuristically, we'll use an image if there is one
        // in any of the response messages, or we'll use all the text from them concatenated, otherwise.

        ChatMessage? lastMessage = chatResponse.Messages.LastOrDefault();

        ContentBlock? content = null;
        if (lastMessage is not null)
        {
            foreach (var lmc in lastMessage.Contents)
            {
                if (lmc is DataContent dc && (dc.HasTopLevelMediaType("image") || dc.HasTopLevelMediaType("audio")))
                {
                    content = dc.ToContent();
                }
            }
        }

        return new()
        {
            Content = content ?? new TextContentBlock { Text = lastMessage?.Text ?? string.Empty },
            Model = chatResponse.ModelId ?? "unknown",
            Role = lastMessage?.Role == ChatRole.User ? Role.User : Role.Assistant,
            StopReason = chatResponse.FinishReason == ChatFinishReason.Length ? "maxTokens" : "endTurn",
        };
    }
}