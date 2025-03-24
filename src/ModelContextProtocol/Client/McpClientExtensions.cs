using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ModelContextProtocol.Client;

/// <summary>
/// Provides extensions for operating on MCP clients.
/// </summary>
public static class McpClientExtensions
{
    /// <summary>
    /// Sends a notification to the server with parameters.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="method">The notification method name.</param>
    /// <param name="parameters">The parameters to send with the notification.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static Task SendNotificationAsync(this IMcpClient client, string method, object? parameters = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        return client.SendMessageAsync(
            new JsonRpcNotification { Method = method, Params = parameters },
            cancellationToken);
    }

    /// <summary>
    /// Sends a ping request to verify server connectivity.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the ping is successful.</returns>
    public static Task PingAsync(this IMcpClient client, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        return client.SendRequestAsync<dynamic>(
            CreateRequest("ping", null),
            cancellationToken);
    }

    /// <summary>
    /// Retrieves a sequence of available tools from the server.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An asynchronous sequence of tool information.</returns>
    public static async IAsyncEnumerable<Tool> ListToolsAsync(
        this IMcpClient client, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var tools = await ListToolsAsync(client, cursor, cancellationToken).ConfigureAwait(false);
            foreach (var tool in tools.Tools)
            {
                yield return tool;
            }

            cursor = tools.NextCursor;
        }
        while (cursor is not null);
    }

    /// <summary>
    /// Retrieves a sequence of available tools from the server.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="cursor">A cursor to paginate the results.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task containing the server's response with tool information.</returns>
    public static Task<ListToolsResult> ListToolsAsync(this IMcpClient client, string? cursor, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        return client.SendRequestAsync<ListToolsResult>(
            CreateRequest("tools/list", CreateCursorDictionary(cursor)),
            cancellationToken);
    }

    /// <summary>
    /// Retrieves a list of available prompts from the server.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An asynchronous sequence of prompt information.</returns>
    public static async IAsyncEnumerable<Prompt> ListPromptsAsync(
        this IMcpClient client, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var prompts = await ListPromptsAsync(client, cursor, cancellationToken).ConfigureAwait(false);
            foreach (var prompt in prompts.Prompts)
            {
                yield return prompt;
            }

            cursor = prompts.NextCursor;
        }
        while (cursor is not null);
    }

    /// <summary>
    /// Retrieves a list of available prompts from the server.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="cursor">A  cursor to paginate the results.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task containing the server's response with prompt information.</returns>
    public static Task<ListPromptsResult> ListPromptsAsync(this IMcpClient client, string? cursor, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        return client.SendRequestAsync<ListPromptsResult>(
            CreateRequest("prompts/list", CreateCursorDictionary(cursor)),
            cancellationToken);
    }

    /// <summary>
    /// Retrieves a specific prompt with optional arguments.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="name">The name of the prompt to retrieve</param>
    /// <param name="arguments">Optional arguments for the prompt</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task containing the prompt's content and messages.</returns>
    public static Task<GetPromptResult> GetPromptAsync(this IMcpClient client, string name, Dictionary<string, object>? arguments = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        return client.SendRequestAsync<GetPromptResult>(
            CreateRequest("prompts/get", CreateParametersDictionary(name, arguments)),
            cancellationToken);
    }

    /// <summary>
    /// Retrieves a sequence of available resource templates from the server.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An asynchronous sequence of resource template information.</returns>
    public static async IAsyncEnumerable<ResourceTemplate> ListResourceTemplatesAsync(
        this IMcpClient client, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var resources = await ListResourceTemplatesAsync(client, cursor, cancellationToken).ConfigureAwait(false);
            foreach (var resource in resources.ResourceTemplates)
            {
                yield return resource;
            }

            cursor = resources.NextCursor;
        }
        while (cursor is not null);
    }

    /// <summary>
    /// Retrieves a list of available resources from the server.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="cursor">A  cursor to paginate the results.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static Task<ListResourceTemplatesResult> ListResourceTemplatesAsync(this IMcpClient client, string? cursor, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        return client.SendRequestAsync<ListResourceTemplatesResult>(
            CreateRequest("resources/templates/list", CreateCursorDictionary(cursor)),
            cancellationToken);
    }

    /// <summary>
    /// Retrieves a sequence of available resources from the server.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An asynchronous sequence of resource information.</returns>
    public static async IAsyncEnumerable<Resource> ListResourcesAsync(
        this IMcpClient client, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var resources = await ListResourcesAsync(client, cursor, cancellationToken).ConfigureAwait(false);
            foreach (var resource in resources.Resources)
            {
                yield return resource;
            }

            cursor = resources.NextCursor;
        }
        while (cursor is not null);
    }

    /// <summary>
    /// Retrieves a list of available resources from the server.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="cursor">A  cursor to paginate the results.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static Task<ListResourcesResult> ListResourcesAsync(this IMcpClient client, string? cursor, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        return client.SendRequestAsync<ListResourcesResult>(
            CreateRequest("resources/list", CreateCursorDictionary(cursor)),
            cancellationToken);
    }

    /// <summary>
    /// Reads a resource from the server.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="uri">The uri of the resource.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static Task<ReadResourceResult> ReadResourceAsync(this IMcpClient client, string uri, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        return client.SendRequestAsync<ReadResourceResult>(
            CreateRequest("resources/read", new() { ["uri"] = uri }),
            cancellationToken);
    }

    /// <summary>
    /// Gets the completion options for a resource or prompt reference and (named) argument.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="reference">A resource (uri) or prompt (name) reference</param>
    /// <param name="argumentName">Name of argument. Must be non-null and non-empty.</param>
    /// <param name="argumentValue">Value of argument. Must be non-null.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static Task<CompleteResult> GetCompletionAsync(this IMcpClient client, Reference reference, string argumentName, string argumentValue, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);
        Throw.IfNull(reference);
        Throw.IfNullOrWhiteSpace(argumentName);

        if (!reference.Validate(out string? validationMessage))
        {
            throw new ArgumentException($"Invalid reference: {validationMessage}", nameof(reference));
        }

        return client.SendRequestAsync<CompleteResult>(
            CreateRequest("completion/complete", new()
            {
                ["ref"] = reference,
                ["argument"] = new Argument { Name = argumentName, Value = argumentValue }
            }),
            cancellationToken);
    }

    /// <summary>
    /// Subscribes to a resource on the server.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="uri">The uri of the resource.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static Task SubscribeToResourceAsync(this IMcpClient client, string uri, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        return client.SendRequestAsync<EmptyResult>(
            CreateRequest("resources/subscribe", new() { ["uri"] = uri }),
            cancellationToken);
    }

    /// <summary>
    /// Unsubscribes from a resource on the server.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="uri">The uri of the resource.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static Task UnsubscribeFromResourceAsync(this IMcpClient client, string uri, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        return client.SendRequestAsync<EmptyResult>(
            CreateRequest("resources/unsubscribe", new() { ["uri"] = uri }),
            cancellationToken);
    }

    /// <summary>
    /// Invokes a tool on the server with optional arguments.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="toolName">The name of the tool to call.</param>
    /// <param name="arguments">Optional arguments for the tool.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task containing the tool's response.</returns>
    public static Task<CallToolResponse> CallToolAsync(this IMcpClient client, string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        return client.SendRequestAsync<CallToolResponse>(
            CreateRequest("tools/call", CreateParametersDictionary(toolName, arguments)),
            cancellationToken);
    }

    /// <summary>Gets <see cref="AIFunction"/> instances for all of the tools available through the specified <see cref="IMcpClient"/>.</summary>
    /// <param name="client">The client for which <see cref="AIFunction"/> instances should be created.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task containing a list of the available functions.</returns>
    public static async Task<IList<AIFunction>> GetAIFunctionsAsync(this IMcpClient client, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        List<AIFunction> functions = [];
        await foreach (var tool in client.ListToolsAsync(cancellationToken).ConfigureAwait(false))
        {
            functions.Add(AsAIFunction(client, tool));
        }

        return functions;
    }

    /// <summary>Gets an <see cref="AIFunction"/> for invoking <see cref="Tool"/> via this <see cref="IMcpClient"/>.</summary>
    /// <param name="client">The client with which to perform the invocation.</param>
    /// <param name="tool">The tool to be invoked.</param>
    /// <returns>An <see cref="AIFunction"/> for performing the call.</returns>
    /// <remarks>
    /// This operation does not validate that <paramref name="tool"/> is valid for the specified <paramref name="client"/>.
    /// If the tool is not valid for the client, it will fail when invoked.
    /// </remarks>
    public static AIFunction AsAIFunction(this IMcpClient client, Tool tool)
    {
        Throw.IfNull(client);
        Throw.IfNull(tool);

        return new McpAIFunction(client, tool);
    }

    /// <summary>
    /// Converts the contents of a <see cref="CreateMessageRequestParams"/> into a pair of
    /// <see cref="IEnumerable{ChatMessage}"/> and <see cref="ChatOptions"/> instances to use
    /// as inputs into a <see cref="IChatClient"/> operation.
    /// </summary>
    /// <param name="requestParams"></param>
    /// <returns>The created pair of messages and options.</returns>
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

        List<ChatMessage> messages = [];
        foreach (SamplingMessage sm in requestParams.Messages)
        {
            ChatMessage message = new()
            {
                Role = sm.Role == Role.User ? ChatRole.User : ChatRole.Assistant,
            };

            if (sm.Content is { Type: "text" })
            {
                message.Contents.Add(new TextContent(sm.Content.Text));
            }
            else if (sm.Content is { Type: "image", MimeType: not null, Data: not null })
            {
                message.Contents.Add(new DataContent(Convert.FromBase64String(sm.Content.Data), sm.Content.MimeType));
            }
            else if (sm.Content is { Type: "resource", Resource: not null })
            {
                ResourceContents resource = sm.Content.Resource;

                if (resource.Text is not null)
                {
                    message.Contents.Add(new TextContent(resource.Text));
                }

                if (resource.Blob is not null && resource.MimeType is not null)
                {
                    message.Contents.Add(new DataContent(Convert.FromBase64String(resource.Blob), resource.MimeType));
                }
            }

            messages.Add(message);
        }

        return (messages, options);
    }

    /// <summary>Converts the contents of a <see cref="ChatResponse"/> into a <see cref="CreateMessageResult"/>.</summary>
    /// <param name="chatResponse">The <see cref="ChatResponse"/> whose contents should be extracted.</param>
    /// <returns>The created <see cref="CreateMessageResult"/>.</returns>
    internal static CreateMessageResult ToCreateMessageResult(this ChatResponse chatResponse)
    {
        Throw.IfNull(chatResponse);

        // The ChatResponse can include multiple messages, of varying modalities, but CreateMessageResult supports
        // only either a single blob of text or a single image. Heuristically, we'll use an image if there is one
        // in any of the response messages, or we'll use all the text from them concatenated, otherwise.

        ChatMessage? lastMessage = chatResponse.Messages.LastOrDefault();

        Content? content = null;
        if (lastMessage is not null)
        {
            foreach (var lmc in lastMessage.Contents)
            {
                if (lmc is DataContent dc && dc.HasTopLevelMediaType("image"))
                {
                    content = new()
                    {
                        Type = "image",
                        MimeType = dc.MediaType,
                        Data = Convert.ToBase64String(dc.Data
#if NET
                            .Span),
#else
                            .ToArray()),
#endif
                    };
                }
            }
        }

        content ??= new()
        {
            Text = lastMessage?.Text ?? string.Empty,
            Type = "text",
        };

        return new()
        {
            Content = content,
            Model = chatResponse.ModelId ?? "unknown",
            Role = lastMessage?.Role == ChatRole.User ? "user" : "assistant",
            StopReason = chatResponse.FinishReason == ChatFinishReason.Length ? "maxTokens" : "endTurn",
        };
    }

    /// <summary>
    /// Creates a sampling handler for use with <see cref="SamplingCapability.SamplingHandler"/> that will
    /// satisfy sampling requests using the specified <see cref="IChatClient"/>.
    /// </summary>
    /// <param name="chatClient">The <see cref="IChatClient"/> with which to satisfy sampling requests.</param>
    /// <returns>The created handler delegate.</returns>
    public static Func<CreateMessageRequestParams?, CancellationToken, Task<CreateMessageResult>> CreateSamplingHandler(this IChatClient chatClient)
    {
        Throw.IfNull(chatClient);

        return async (requestParams, cancellationToken) =>
        {
            Throw.IfNull(requestParams);

            var (messages, options) = requestParams.ToChatClientArguments();
            var response = await chatClient.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            return response.ToCreateMessageResult();
        };
    }

    /// <summary>
    /// Configures the minimum logging level for the server.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="level">The minimum log level of messages to be generated.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static Task SetLoggingLevel(this IMcpClient client, LoggingLevel level, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        return client.SendRequestAsync<EmptyResult>(
            CreateRequest("logging/setLevel", new() { ["level"] = level }),
            cancellationToken);
    }

    private static JsonRpcRequest CreateRequest(string method, Dictionary<string, object?>? parameters) =>
        new()
        {
            Method = method,
            Params = parameters
        };

    private static Dictionary<string, object?>? CreateCursorDictionary(string? cursor) =>
        cursor != null ? new() { ["cursor"] = cursor } : null;

    private static Dictionary<string, object?> CreateParametersDictionary(string nameParameter, Dictionary<string, object>? arguments)
    {
        Dictionary<string, object?> parameters = new()
        {
            ["name"] = nameParameter
        };

        if (arguments != null)
        {
            parameters["arguments"] = arguments;
        }

        return parameters;
    }

    /// <summary>Provides an AI function that calls a tool through <see cref="IMcpClient"/>.</summary>
    private sealed class McpAIFunction(IMcpClient client, Tool tool) : AIFunction
    {
        /// <inheritdoc/>
        public override string Name => tool.Name;

        /// <inheritdoc/>
        public override string Description => tool.Description ?? string.Empty;

        /// <inheritdoc/>
        public override JsonElement JsonSchema => tool.InputSchema;

        /// <inheritdoc/>
        protected async override Task<object?> InvokeCoreAsync(
            IEnumerable<KeyValuePair<string, object?>> arguments, CancellationToken cancellationToken)
        {
            Throw.IfNull(arguments);

            Dictionary<string, object> argDict = [];
            foreach (var arg in arguments)
            {
                if (arg.Value is not null)
                {
                    argDict[arg.Key] = arg.Value;
                }
            }

            CallToolResponse result = await client.CallToolAsync(tool.Name, argDict, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.SerializeToElement(result, McpJsonUtilities.JsonContext.Default.CallToolResponse);
        }
    }
}