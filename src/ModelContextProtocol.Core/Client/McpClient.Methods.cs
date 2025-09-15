using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ModelContextProtocol.Client;

/// <summary>
/// Represents an instance of a Model Context Protocol (MCP) client session that connects to and communicates with an MCP server.
/// </summary>
#pragma warning disable CS0618 // Type or member is obsolete
public abstract partial class McpClient : McpSession, IMcpClient
#pragma warning restore CS0618 // Type or member is obsolete
{
    /// <summary>Creates an <see cref="McpClient"/>, connecting it to the specified server.</summary>
    /// <param name="clientTransport">The transport instance used to communicate with the server.</param>
    /// <param name="clientOptions">
    /// A client configuration object which specifies client capabilities and protocol version.
    /// If <see langword="null"/>, details based on the current process will be employed.
    /// </param>
    /// <param name="loggerFactory">A logger factory for creating loggers for clients.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>An <see cref="McpClient"/> that's connected to the specified server.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clientTransport"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="clientOptions"/> is <see langword="null"/>.</exception>
    public static async Task<McpClient> CreateAsync(
        IClientTransport clientTransport,
        McpClientOptions? clientOptions = null,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(clientTransport);

        var transport = await clientTransport.ConnectAsync(cancellationToken).ConfigureAwait(false);
        var endpointName = clientTransport.Name;

        var clientSession = new McpClientImpl(transport, endpointName, clientOptions, loggerFactory);
        try
        {
            await clientSession.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await clientSession.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return clientSession;
    }

    /// <summary>
    /// Sends a ping request to verify server connectivity.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that completes when the ping is successful.</returns>
    /// <exception cref="McpException">Thrown when the server cannot be reached or returns an error response.</exception>
    public Task PingAsync(CancellationToken cancellationToken = default)
    {
        var opts = McpJsonUtilities.DefaultOptions;
        opts.MakeReadOnly();
        return SendRequestAsync<object?, object>(
            RequestMethods.Ping,
            parameters: null,
            serializerOptions: opts,
            cancellationToken: cancellationToken).AsTask();
    }

    /// <summary>
    /// Retrieves a list of available tools from the server.
    /// </summary>
    /// <param name="serializerOptions">The serializer options governing tool parameter serialization. If null, the default options will be used.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A list of all available tools as <see cref="McpClientTool"/> instances.</returns>
    public async ValueTask<IList<McpClientTool>> ListToolsAsync(
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        serializerOptions ??= McpJsonUtilities.DefaultOptions;
        serializerOptions.MakeReadOnly();

        List<McpClientTool>? tools = null;
        string? cursor = null;
        do
        {
            var toolResults = await SendRequestAsync(
                RequestMethods.ToolsList,
                new() { Cursor = cursor },
                McpJsonUtilities.JsonContext.Default.ListToolsRequestParams,
                McpJsonUtilities.JsonContext.Default.ListToolsResult,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            tools ??= new List<McpClientTool>(toolResults.Tools.Count);
            foreach (var tool in toolResults.Tools)
            {
                tools.Add(new McpClientTool(this, tool, serializerOptions));
            }

            cursor = toolResults.NextCursor;
        }
        while (cursor is not null);

        return tools;
    }

    /// <summary>
    /// Creates an enumerable for asynchronously enumerating all available tools from the server.
    /// </summary>
    /// <param name="serializerOptions">The serializer options governing tool parameter serialization. If null, the default options will be used.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>An asynchronous sequence of all available tools as <see cref="McpClientTool"/> instances.</returns>
    public async IAsyncEnumerable<McpClientTool> EnumerateToolsAsync(
        JsonSerializerOptions? serializerOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        serializerOptions ??= McpJsonUtilities.DefaultOptions;
        serializerOptions.MakeReadOnly();

        string? cursor = null;
        do
        {
            var toolResults = await SendRequestAsync(
                RequestMethods.ToolsList,
                new() { Cursor = cursor },
                McpJsonUtilities.JsonContext.Default.ListToolsRequestParams,
                McpJsonUtilities.JsonContext.Default.ListToolsResult,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var tool in toolResults.Tools)
            {
                yield return new McpClientTool(this, tool, serializerOptions);
            }

            cursor = toolResults.NextCursor;
        }
        while (cursor is not null);
    }

    /// <summary>
    /// Retrieves a list of available prompts from the server.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A list of all available prompts as <see cref="McpClientPrompt"/> instances.</returns>
    public async ValueTask<IList<McpClientPrompt>> ListPromptsAsync(
        CancellationToken cancellationToken = default)
    {
        List<McpClientPrompt>? prompts = null;
        string? cursor = null;
        do
        {
            var promptResults = await SendRequestAsync(
                RequestMethods.PromptsList,
                new() { Cursor = cursor },
                McpJsonUtilities.JsonContext.Default.ListPromptsRequestParams,
                McpJsonUtilities.JsonContext.Default.ListPromptsResult,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            prompts ??= new List<McpClientPrompt>(promptResults.Prompts.Count);
            foreach (var prompt in promptResults.Prompts)
            {
                prompts.Add(new McpClientPrompt(this, prompt));
            }

            cursor = promptResults.NextCursor;
        }
        while (cursor is not null);

        return prompts;
    }

    /// <summary>
    /// Creates an enumerable for asynchronously enumerating all available prompts from the server.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>An asynchronous sequence of all available prompts as <see cref="McpClientPrompt"/> instances.</returns>
    public async IAsyncEnumerable<McpClientPrompt> EnumeratePromptsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var promptResults = await SendRequestAsync(
                RequestMethods.PromptsList,
                new() { Cursor = cursor },
                McpJsonUtilities.JsonContext.Default.ListPromptsRequestParams,
                McpJsonUtilities.JsonContext.Default.ListPromptsResult,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var prompt in promptResults.Prompts)
            {
                yield return new(this, prompt);
            }

            cursor = promptResults.NextCursor;
        }
        while (cursor is not null);
    }

    /// <summary>
    /// Retrieves a specific prompt from the MCP server.
    /// </summary>
    /// <param name="name">The name of the prompt to retrieve.</param>
    /// <param name="arguments">Optional arguments for the prompt. Keys are parameter names, and values are the argument values.</param>
    /// <param name="serializerOptions">The serialization options governing argument serialization.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task containing the prompt's result with content and messages.</returns>
    public ValueTask<GetPromptResult> GetPromptAsync(
        string name,
        IReadOnlyDictionary<string, object?>? arguments = null,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(name);

        serializerOptions ??= McpJsonUtilities.DefaultOptions;
        serializerOptions.MakeReadOnly();

        return SendRequestAsync(
            RequestMethods.PromptsGet,
            new() { Name = name, Arguments = ToArgumentsDictionary(arguments, serializerOptions) },
            McpJsonUtilities.JsonContext.Default.GetPromptRequestParams,
            McpJsonUtilities.JsonContext.Default.GetPromptResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Retrieves a list of available resource templates from the server.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A list of all available resource templates as <see cref="ResourceTemplate"/> instances.</returns>
    public async ValueTask<IList<McpClientResourceTemplate>> ListResourceTemplatesAsync(
        CancellationToken cancellationToken = default)
    {
        List<McpClientResourceTemplate>? resourceTemplates = null;

        string? cursor = null;
        do
        {
            var templateResults = await SendRequestAsync(
                RequestMethods.ResourcesTemplatesList,
                new() { Cursor = cursor },
                McpJsonUtilities.JsonContext.Default.ListResourceTemplatesRequestParams,
                McpJsonUtilities.JsonContext.Default.ListResourceTemplatesResult,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            resourceTemplates ??= new List<McpClientResourceTemplate>(templateResults.ResourceTemplates.Count);
            foreach (var template in templateResults.ResourceTemplates)
            {
                resourceTemplates.Add(new McpClientResourceTemplate(this, template));
            }

            cursor = templateResults.NextCursor;
        }
        while (cursor is not null);

        return resourceTemplates;
    }

    /// <summary>
    /// Creates an enumerable for asynchronously enumerating all available resource templates from the server.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>An asynchronous sequence of all available resource templates as <see cref="ResourceTemplate"/> instances.</returns>
    public async IAsyncEnumerable<McpClientResourceTemplate> EnumerateResourceTemplatesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var templateResults = await SendRequestAsync(
                RequestMethods.ResourcesTemplatesList,
                new() { Cursor = cursor },
                McpJsonUtilities.JsonContext.Default.ListResourceTemplatesRequestParams,
                McpJsonUtilities.JsonContext.Default.ListResourceTemplatesResult,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var templateResult in templateResults.ResourceTemplates)
            {
                yield return new McpClientResourceTemplate(this, templateResult);
            }

            cursor = templateResults.NextCursor;
        }
        while (cursor is not null);
    }

    /// <summary>
    /// Retrieves a list of available resources from the server.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A list of all available resources as <see cref="Resource"/> instances.</returns>
    public async ValueTask<IList<McpClientResource>> ListResourcesAsync(
        CancellationToken cancellationToken = default)
    {
        List<McpClientResource>? resources = null;

        string? cursor = null;
        do
        {
            var resourceResults = await SendRequestAsync(
                RequestMethods.ResourcesList,
                new() { Cursor = cursor },
                McpJsonUtilities.JsonContext.Default.ListResourcesRequestParams,
                McpJsonUtilities.JsonContext.Default.ListResourcesResult,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            resources ??= new List<McpClientResource>(resourceResults.Resources.Count);
            foreach (var resource in resourceResults.Resources)
            {
                resources.Add(new McpClientResource(this, resource));
            }

            cursor = resourceResults.NextCursor;
        }
        while (cursor is not null);

        return resources;
    }

    /// <summary>
    /// Creates an enumerable for asynchronously enumerating all available resources from the server.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>An asynchronous sequence of all available resources as <see cref="Resource"/> instances.</returns>
    public async IAsyncEnumerable<McpClientResource> EnumerateResourcesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var resourceResults = await SendRequestAsync(
                RequestMethods.ResourcesList,
                new() { Cursor = cursor },
                McpJsonUtilities.JsonContext.Default.ListResourcesRequestParams,
                McpJsonUtilities.JsonContext.Default.ListResourcesResult,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var resource in resourceResults.Resources)
            {
                yield return new McpClientResource(this, resource);
            }

            cursor = resourceResults.NextCursor;
        }
        while (cursor is not null);
    }

    /// <summary>
    /// Reads a resource from the server.
    /// </summary>
    /// <param name="uri">The uri of the resource.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    public ValueTask<ReadResourceResult> ReadResourceAsync(
        string uri, CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(uri);

        return SendRequestAsync(
            RequestMethods.ResourcesRead,
            new() { Uri = uri },
            McpJsonUtilities.JsonContext.Default.ReadResourceRequestParams,
            McpJsonUtilities.JsonContext.Default.ReadResourceResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Reads a resource from the server.
    /// </summary>
    /// <param name="uri">The uri of the resource.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    public ValueTask<ReadResourceResult> ReadResourceAsync(
        Uri uri, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(uri);

        return ReadResourceAsync(uri.ToString(), cancellationToken);
    }

    /// <summary>
    /// Reads a resource from the server.
    /// </summary>
    /// <param name="uriTemplate">The uri template of the resource.</param>
    /// <param name="arguments">Arguments to use to format <paramref name="uriTemplate"/>.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    public ValueTask<ReadResourceResult> ReadResourceAsync(
        string uriTemplate, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(uriTemplate);
        Throw.IfNull(arguments);

        return SendRequestAsync(
            RequestMethods.ResourcesRead,
            new() { Uri = UriTemplate.FormatUri(uriTemplate, arguments) },
            McpJsonUtilities.JsonContext.Default.ReadResourceRequestParams,
            McpJsonUtilities.JsonContext.Default.ReadResourceResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Requests completion suggestions for a prompt argument or resource reference.
    /// </summary>
    /// <param name="reference">The reference object specifying the type and optional URI or name.</param>
    /// <param name="argumentName">The name of the argument for which completions are requested.</param>
    /// <param name="argumentValue">The current value of the argument, used to filter relevant completions.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="CompleteResult"/> containing completion suggestions.</returns>
    public ValueTask<CompleteResult> CompleteAsync(Reference reference, string argumentName, string argumentValue, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(reference);
        Throw.IfNullOrWhiteSpace(argumentName);

        return SendRequestAsync(
            RequestMethods.CompletionComplete,
            new()
            {
                Ref = reference,
                Argument = new Argument { Name = argumentName, Value = argumentValue }
            },
            McpJsonUtilities.JsonContext.Default.CompleteRequestParams,
            McpJsonUtilities.JsonContext.Default.CompleteResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Subscribes to a resource on the server to receive notifications when it changes.
    /// </summary>
    /// <param name="uri">The URI of the resource to which to subscribe.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task SubscribeToResourceAsync(string uri, CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(uri);

        return SendRequestAsync(
            RequestMethods.ResourcesSubscribe,
            new() { Uri = uri },
            McpJsonUtilities.JsonContext.Default.SubscribeRequestParams,
            McpJsonUtilities.JsonContext.Default.EmptyResult,
            cancellationToken: cancellationToken).AsTask();
    }

    /// <summary>
    /// Subscribes to a resource on the server to receive notifications when it changes.
    /// </summary>
    /// <param name="uri">The URI of the resource to which to subscribe.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task SubscribeToResourceAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(uri);

        return SubscribeToResourceAsync(uri.ToString(), cancellationToken);
    }

    /// <summary>
    /// Unsubscribes from a resource on the server to stop receiving notifications about its changes.
    /// </summary>
    /// <param name="uri">The URI of the resource to unsubscribe from.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task UnsubscribeFromResourceAsync(string uri, CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(uri);

        return SendRequestAsync(
            RequestMethods.ResourcesUnsubscribe,
            new() { Uri = uri },
            McpJsonUtilities.JsonContext.Default.UnsubscribeRequestParams,
            McpJsonUtilities.JsonContext.Default.EmptyResult,
            cancellationToken: cancellationToken).AsTask();
    }

    /// <summary>
    /// Unsubscribes from a resource on the server to stop receiving notifications about its changes.
    /// </summary>
    /// <param name="uri">The URI of the resource to unsubscribe from.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task UnsubscribeFromResourceAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(uri);

        return UnsubscribeFromResourceAsync(uri.ToString(), cancellationToken);
    }

    /// <summary>
    /// Invokes a tool on the server.
    /// </summary>
    /// <param name="toolName">The name of the tool to call on the server..</param>
    /// <param name="arguments">An optional dictionary of arguments to pass to the tool.</param>
    /// <param name="progress">Optional progress reporter for server notifications.</param>
    /// <param name="serializerOptions">JSON serializer options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The <see cref="CallToolResult"/> from the tool execution.</returns>
    public ValueTask<CallToolResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        IProgress<ProgressNotificationValue>? progress = null,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(toolName);
        serializerOptions ??= McpJsonUtilities.DefaultOptions;
        serializerOptions.MakeReadOnly();

        if (progress is not null)
        {
            return SendRequestWithProgressAsync(toolName, arguments, progress, serializerOptions, cancellationToken);
        }

        return SendRequestAsync(
            RequestMethods.ToolsCall,
            new()
            {
                Name = toolName,
                Arguments = ToArgumentsDictionary(arguments, serializerOptions),
            },
            McpJsonUtilities.JsonContext.Default.CallToolRequestParams,
            McpJsonUtilities.JsonContext.Default.CallToolResult,
            cancellationToken: cancellationToken);

        async ValueTask<CallToolResult> SendRequestWithProgressAsync(
            string toolName,
            IReadOnlyDictionary<string, object?>? arguments,
            IProgress<ProgressNotificationValue> progress,
            JsonSerializerOptions serializerOptions,
            CancellationToken cancellationToken)
        {
            ProgressToken progressToken = new(Guid.NewGuid().ToString("N"));

            await using var _ = RegisterNotificationHandler(NotificationMethods.ProgressNotification,
                (notification, cancellationToken) =>
                {
                    if (JsonSerializer.Deserialize(notification.Params, McpJsonUtilities.JsonContext.Default.ProgressNotificationParams) is { } pn &&
                        pn.ProgressToken == progressToken)
                    {
                        progress.Report(pn.Progress);
                    }

                    return default;
                }).ConfigureAwait(false);

            return await SendRequestAsync(
                RequestMethods.ToolsCall,
                new()
                {
                    Name = toolName,
                    Arguments = ToArgumentsDictionary(arguments, serializerOptions),
                    ProgressToken = progressToken,
                },
                McpJsonUtilities.JsonContext.Default.CallToolRequestParams,
                McpJsonUtilities.JsonContext.Default.CallToolResult,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
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
        CreateMessageRequestParams requestParams)
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
    internal static CreateMessageResult ToCreateMessageResult(ChatResponse chatResponse)
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

    /// <summary>
    /// Creates a sampling handler for use with <see cref="SamplingCapability.SamplingHandler"/> that will
    /// satisfy sampling requests using the specified <see cref="IChatClient"/>.
    /// </summary>
    /// <param name="chatClient">The <see cref="IChatClient"/> with which to satisfy sampling requests.</param>
    /// <returns>The created handler delegate that can be assigned to <see cref="SamplingCapability.SamplingHandler"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="chatClient"/> is <see langword="null"/>.</exception>
    public static Func<CreateMessageRequestParams?, IProgress<ProgressNotificationValue>, CancellationToken, ValueTask<CreateMessageResult>> CreateSamplingHandler(
        IChatClient chatClient)
    {
        Throw.IfNull(chatClient);

        return async (requestParams, progress, cancellationToken) =>
        {
            Throw.IfNull(requestParams);

            var (messages, options) = ToChatClientArguments(requestParams);
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

            return ToCreateMessageResult(updates.ToChatResponse());
        };
    }

    /// <summary>
    /// Sets the logging level for the server to control which log messages are sent to the client.
    /// </summary>
    /// <param name="level">The minimum severity level of log messages to receive from the server.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task SetLoggingLevel(LoggingLevel level, CancellationToken cancellationToken = default)
    {
        return SendRequestAsync(
            RequestMethods.LoggingSetLevel,
            new() { Level = level },
            McpJsonUtilities.JsonContext.Default.SetLevelRequestParams,
            McpJsonUtilities.JsonContext.Default.EmptyResult,
            cancellationToken: cancellationToken).AsTask();
    }

    /// <summary>
    /// Sets the logging level for the server to control which log messages are sent to the client.
    /// </summary>
    /// <param name="level">The minimum severity level of log messages to receive from the server.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task SetLoggingLevel(LogLevel level, CancellationToken cancellationToken = default) =>
        SetLoggingLevel(McpServerImpl.ToLoggingLevel(level), cancellationToken);

    /// <summary>Convers a dictionary with <see cref="object"/> values to a dictionary with <see cref="JsonElement"/> values.</summary>
    private static Dictionary<string, JsonElement>? ToArgumentsDictionary(
        IReadOnlyDictionary<string, object?>? arguments, JsonSerializerOptions options)
    {
        var typeInfo = options.GetTypeInfo<object?>();

        Dictionary<string, JsonElement>? result = null;
        if (arguments is not null)
        {
            result = new(arguments.Count);
            foreach (var kvp in arguments)
            {
                result.Add(kvp.Key, kvp.Value is JsonElement je ? je : JsonSerializer.SerializeToElement(kvp.Value, typeInfo));
            }
        }

        return result;
    }
}
