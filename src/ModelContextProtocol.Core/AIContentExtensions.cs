using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol;

/// <summary>
/// Provides extension methods for converting between Model Context Protocol (MCP) types and Microsoft.Extensions.AI types.
/// </summary>
/// <remarks>
/// This class serves as an adapter layer between Model Context Protocol (MCP) types and the <see cref="AIContent"/> model types
/// from the Microsoft.Extensions.AI namespace.
/// </remarks>
public static class AIContentExtensions
{
    /// <summary>
    /// Creates a sampling handler for use with <see cref="McpClientHandlers.SamplingHandler"/> that will
    /// satisfy sampling requests using the specified <see cref="IChatClient"/>.
    /// </summary>
    /// <param name="chatClient">The <see cref="IChatClient"/> with which to satisfy sampling requests.</param>
    /// <param name="serializerOptions">The <see cref="JsonSerializerOptions"/> to use for serializing user-provided objects. If <see langword="null"/>, <see cref="McpJsonUtilities.DefaultOptions"/> is used.</param>
    /// <returns>The created handler delegate that can be assigned to <see cref="McpClientHandlers.SamplingHandler"/>.</returns>
    /// <remarks>
    /// <para>
    /// This method creates a function that converts MCP message requests into chat client calls, enabling
    /// an MCP client to generate text or other content using an actual AI model via the provided chat client.
    /// </para>
    /// <para>
    /// The handler can process text messages, image messages, resource messages, and tool use/results as defined in the
    /// Model Context Protocol.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="chatClient"/> is <see langword="null"/>.</exception>
    public static Func<CreateMessageRequestParams?, IProgress<ProgressNotificationValue>, CancellationToken, ValueTask<CreateMessageResult>> CreateSamplingHandler(
        this IChatClient chatClient,
        JsonSerializerOptions? serializerOptions = null)
    {
        Throw.IfNull(chatClient);

        serializerOptions ??= McpJsonUtilities.DefaultOptions;

        return async (requestParams, progress, cancellationToken) =>
        {
            Throw.IfNull(requestParams);

            var (messages, options) = ToChatClientArguments(requestParams, serializerOptions);
            var progressToken = requestParams.ProgressToken;

            List<ChatResponseUpdate> updates = [];
            await foreach (var update in chatClient.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                updates.Add(update);

                if (progressToken is not null)
                {
                    progress.Report(new() { Progress = updates.Count });
                }
            }

            ChatResponse? chatResponse = updates.ToChatResponse();
            ChatMessage? lastMessage = chatResponse.Messages.LastOrDefault();

            IList<ContentBlock>? contents = lastMessage?.Contents.Select(c => c.ToContentBlock()).ToList();
            if (contents is not { Count: > 0 })
            {
                (contents ??= []).Add(new TextContentBlock() { Text = "" });
            }

            return new()
            {
                Model = chatResponse.ModelId ?? "",
                StopReason =
                    chatResponse.FinishReason == ChatFinishReason.Stop ? CreateMessageResult.StopReasonEndTurn :
                    chatResponse.FinishReason == ChatFinishReason.Length ? CreateMessageResult.StopReasonMaxTokens :
                    chatResponse.FinishReason == ChatFinishReason.ToolCalls ? CreateMessageResult.StopReasonToolUse :
                    chatResponse.FinishReason.ToString(),
                Meta = chatResponse.AdditionalProperties?.ToJsonObject(serializerOptions),
                Role = lastMessage?.Role == ChatRole.User ? Role.User : Role.Assistant,
                Content = contents,
            };

            static (IList<ChatMessage> Messages, ChatOptions? Options) ToChatClientArguments(CreateMessageRequestParams requestParams, JsonSerializerOptions serializerOptions)
            {
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

                if (requestParams.SystemPrompt is { } systemPrompt)
                {
                    (options ??= new()).Instructions = systemPrompt;
                }

                if (requestParams.Tools is { } tools)
                {
                    foreach (var tool in tools)
                    {
                        ((options ??= new()).Tools ??= []).Add(new ToolAIFunctionDeclaration(tool));
                    }

                    if (options.Tools is { Count: > 0 } && requestParams.ToolChoice is { } toolChoice)
                    {
                        options.ToolMode = toolChoice.Mode switch
                        {
                            ToolChoice.ModeAuto => ChatToolMode.Auto,
                            ToolChoice.ModeRequired => ChatToolMode.RequireAny,
                            ToolChoice.ModeNone => ChatToolMode.None,
                            _ => null,
                        };
                    }
                }

                List<ChatMessage> messages = [];
                foreach (var sm in requestParams.Messages)
                {
                    if (sm.Content?.Select(b => b.ToAIContent(serializerOptions)).OfType<AIContent>().ToList() is { Count: > 0 } aiContents)
                    {
                        ChatRole role =
                            aiContents.All(static c => c is FunctionResultContent) ? ChatRole.Tool :
                            sm.Role is Role.Assistant ? ChatRole.Assistant :
                            ChatRole.User;
                        messages.Add(new ChatMessage(role, aiContents));
                    }
                }

                return (messages, options);
            }
        };
    }

    /// <summary>Converts the specified dictionary to a <see cref="JsonObject"/>.</summary>
    internal static JsonObject? ToJsonObject(this IReadOnlyDictionary<string, object?> properties, JsonSerializerOptions options)
    {
        return JsonSerializer.SerializeToNode(properties, options.GetTypeInfo(typeof(IReadOnlyDictionary<string, object?>))) as JsonObject;
    }

    internal static AdditionalPropertiesDictionary ToAdditionalProperties(this JsonObject obj)
    {
        AdditionalPropertiesDictionary d = [];
        foreach (var kvp in obj)
        {
            d.Add(kvp.Key, kvp.Value);
        }

        return d;
    }

    /// <summary>
    /// Converts a <see cref="PromptMessage"/> to a <see cref="ChatMessage"/> object.
    /// </summary>
    /// <param name="promptMessage">The prompt message to convert.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions"/> to use for deserialization. If <see langword="null"/>, <see cref="McpJsonUtilities.DefaultOptions"/> is used.</param>
    /// <returns>A <see cref="ChatMessage"/> object created from the prompt message.</returns>
    /// <remarks>
    /// This method transforms a protocol-specific <see cref="PromptMessage"/> from the Model Context Protocol
    /// into a standard <see cref="ChatMessage"/> object that can be used with AI client libraries.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="promptMessage"/> is <see langword="null"/>.</exception>
    public static ChatMessage ToChatMessage(this PromptMessage promptMessage, JsonSerializerOptions? options = null)
    {
        Throw.IfNull(promptMessage);

        AIContent? content = promptMessage.Content.ToAIContent(options);

        return new()
        {
            RawRepresentation = promptMessage,
            Role = promptMessage.Role == Role.User ? ChatRole.User : ChatRole.Assistant,
            Contents = content is not null ? [content] : [],
        };
    }

    /// <summary>
    /// Converts a <see cref="CallToolResult"/> to a <see cref="ChatMessage"/> object.
    /// </summary>
    /// <param name="result">The tool result to convert.</param>
    /// <param name="callId">The identifier for the function call request that triggered the tool invocation.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions"/> to use for serialization. If <see langword="null"/>, <see cref="McpJsonUtilities.DefaultOptions"/> is used.</param>
    /// <returns>A <see cref="ChatMessage"/> object created from the tool result.</returns>
    /// <remarks>
    /// This method transforms a protocol-specific <see cref="CallToolResult"/> from the Model Context Protocol
    /// into a standard <see cref="ChatMessage"/> object that can be used with AI client libraries. It produces a
    /// <see cref="ChatRole.Tool"/> message containing a <see cref="FunctionResultContent"/> with result as a
    /// serialized <see cref="JsonElement"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> or <paramref name="callId"/> is <see langword="null"/>.</exception>
    public static ChatMessage ToChatMessage(this CallToolResult result, string callId, JsonSerializerOptions? options = null)
    {
        Throw.IfNull(result);
        Throw.IfNull(callId);

        options ??= McpJsonUtilities.DefaultOptions;

        return new(ChatRole.Tool, [new FunctionResultContent(callId, JsonSerializer.SerializeToElement(result, options.GetTypeInfo<CallToolResult>()))
        {
             RawRepresentation = result,
        }]);
    }

    /// <summary>
    /// Converts a <see cref="GetPromptResult"/> to a list of <see cref="ChatMessage"/> objects.
    /// </summary>
    /// <param name="promptResult">The prompt result containing messages to convert.</param>
    /// <returns>A list of <see cref="ChatMessage"/> objects created from the prompt messages.</returns>
    /// <remarks>
    /// This method transforms protocol-specific <see cref="PromptMessage"/> objects from a Model Context Protocol
    /// prompt result into standard <see cref="ChatMessage"/> objects that can be used with AI client libraries.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="promptResult"/> is <see langword="null"/>.</exception>
    public static IList<ChatMessage> ToChatMessages(this GetPromptResult promptResult)
    {
        Throw.IfNull(promptResult);

        return promptResult.Messages.Select(m => m.ToChatMessage()).ToList();
    }

    /// <summary>
    /// Converts a <see cref="ChatMessage"/> to a list of <see cref="PromptMessage"/> objects.
    /// </summary>
    /// <param name="chatMessage">The chat message to convert.</param>
    /// <returns>A list of <see cref="PromptMessage"/> objects created from the chat message's contents.</returns>
    /// <remarks>
    /// This method transforms standard <see cref="ChatMessage"/> objects used with AI client libraries into
    /// protocol-specific <see cref="PromptMessage"/> objects for the Model Context Protocol system.
    /// Only representable content items are processed.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="chatMessage"/> is <see langword="null"/>.</exception>
    public static IList<PromptMessage> ToPromptMessages(this ChatMessage chatMessage)
    {
        Throw.IfNull(chatMessage);

        Role r = chatMessage.Role == ChatRole.User ? Role.User : Role.Assistant;

        List<PromptMessage> messages = [];
        foreach (var content in chatMessage.Contents)
        {
            if (content is TextContent or DataContent)
            {
                messages.Add(new PromptMessage { Role = r, Content = content.ToContentBlock() });
            }
        }

        return messages;
    }

    /// <summary>Creates a new <see cref="AIContent"/> from the content of a <see cref="ContentBlock"/>.</summary>
    /// <param name="content">The <see cref="ContentBlock"/> to convert.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions"/> to use for deserialization. If <see langword="null"/>, <see cref="McpJsonUtilities.DefaultOptions"/> is used.</param>
    /// <returns>
    /// The created <see cref="AIContent"/>. If the content can't be converted (such as when it's a resource link), <see langword="null"/> is returned.
    /// </returns>
    /// <remarks>
    /// This method converts Model Context Protocol content types to the equivalent Microsoft.Extensions.AI 
    /// content types, enabling seamless integration between the protocol and AI client libraries.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    public static AIContent? ToAIContent(this ContentBlock content, JsonSerializerOptions? options = null)
    {
        Throw.IfNull(content);

        options ??= McpJsonUtilities.DefaultOptions;

        AIContent? ac = content switch
        {
            TextContentBlock textContent => new TextContent(textContent.Text),
            
            ImageContentBlock imageContent => new DataContent(imageContent.DecodedData, imageContent.MimeType),
            
            AudioContentBlock audioContent => new DataContent(audioContent.DecodedData, audioContent.MimeType),
            
            EmbeddedResourceBlock resourceContent => resourceContent.Resource.ToAIContent(),
            
            ToolUseContentBlock toolUse => FunctionCallContent.CreateFromParsedArguments(toolUse.Input, toolUse.Id, toolUse.Name,
                json => JsonSerializer.Deserialize(json, options.GetTypeInfo<IDictionary<string, object?>>())),
            
            ToolResultContentBlock toolResult => new FunctionResultContent(
                toolResult.ToolUseId,
                toolResult.Content.Count == 1 ? toolResult.Content[0].ToAIContent(options) : toolResult.Content.Select(c => c.ToAIContent(options)).OfType<AIContent>().ToList())
            {
                Exception = toolResult.IsError is true ? new() : null,
            },
            
            _ => null,
        };

        if (ac is not null)
        {
            ac.RawRepresentation = content;
            ac.AdditionalProperties = content.Meta?.ToAdditionalProperties();
        }

        return ac;
    }

    /// <summary>Creates a new <see cref="AIContent"/> from the content of a <see cref="ResourceContents"/>.</summary>
    /// <param name="content">The <see cref="ResourceContents"/> to convert.</param>
    /// <returns>The created <see cref="AIContent"/>.</returns>
    /// <remarks>
    /// This method converts Model Context Protocol resource types to the equivalent Microsoft.Extensions.AI 
    /// content types, enabling seamless integration between the protocol and AI client libraries.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The resource type is not supported.</exception>
    public static AIContent ToAIContent(this ResourceContents content)
    {
        Throw.IfNull(content);

        AIContent ac = content switch
        {
            BlobResourceContents blobResource => new DataContent(blobResource.DecodedData, blobResource.MimeType ?? "application/octet-stream"),
            TextResourceContents textResource => new TextContent(textResource.Text),
            _ => throw new NotSupportedException($"Resource type '{content.GetType().Name}' is not supported.")
        };

        (ac.AdditionalProperties ??= [])["uri"] = content.Uri;
        ac.RawRepresentation = content;

        return ac;
    }

    /// <summary>Creates a list of <see cref="AIContent"/> from a sequence of <see cref="ContentBlock"/>.</summary>
    /// <param name="contents">The <see cref="ContentBlock"/> instances to convert.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions"/> to use for deserialization. If <see langword="null"/>, <see cref="McpJsonUtilities.DefaultOptions"/> is used.</param>
    /// <returns>The created <see cref="AIContent"/> instances.</returns>
    /// <remarks>
    /// <para>
    /// This method converts a collection of Model Context Protocol content objects into a collection of
    /// Microsoft.Extensions.AI content objects. It's useful when working with multiple content items, such as
    /// when processing the contents of a message or response.
    /// </para>
    /// <para>
    /// Each <see cref="ContentBlock"/> object is converted using <see cref="ToAIContent(ContentBlock, JsonSerializerOptions?)"/>,
    /// preserving the type-specific conversion logic for text, images, audio, and resources.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="contents"/> is <see langword="null"/>.</exception>
    public static IList<AIContent> ToAIContents(this IEnumerable<ContentBlock> contents, JsonSerializerOptions? options = null)
    {
        Throw.IfNull(contents);

        return [.. contents.Select(c => c.ToAIContent(options)).OfType<AIContent>()];
    }

    /// <summary>Creates a list of <see cref="AIContent"/> from a sequence of <see cref="ResourceContents"/>.</summary>
    /// <param name="contents">The <see cref="ResourceContents"/> instances to convert.</param>
    /// <returns>A list of <see cref="AIContent"/> objects created from the resource contents.</returns>
    /// <remarks>
    /// <para>
    /// This method converts a collection of Model Context Protocol resource objects into a collection of
    /// Microsoft.Extensions.AI content objects. It's useful when working with multiple resources, such as
    /// when processing the contents of a <see cref="ReadResourceResult"/>.
    /// </para>
    /// <para>
    /// Each <see cref="ResourceContents"/> object is converted using <see cref="ToAIContent(ResourceContents)"/>,
    /// preserving the type-specific conversion logic: text resources become <see cref="TextContent"/> objects and
    /// binary resources become <see cref="DataContent"/> objects.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="contents"/> is <see langword="null"/>.</exception>
    public static IList<AIContent> ToAIContents(this IEnumerable<ResourceContents> contents)
    {
        Throw.IfNull(contents);

        return [.. contents.Select(ToAIContent)];
    }

    /// <summary>Creates a new <see cref="ContentBlock"/> from the content of an <see cref="AIContent"/>.</summary>
    /// <param name="content">The <see cref="AIContent"/> to convert.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions"/> to use for serialization. If <see langword="null"/>, <see cref="McpJsonUtilities.DefaultOptions"/> is used.</param>
    /// <returns>The created <see cref="ContentBlock"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    public static ContentBlock ToContentBlock(this AIContent content, JsonSerializerOptions? options = null)
    {
        Throw.IfNull(content);

        options ??= McpJsonUtilities.DefaultOptions;

        ContentBlock contentBlock = content switch
        {
            TextContent textContent => new TextContentBlock
            {
                Text = textContent.Text,
            },

            DataContent dataContent when dataContent.HasTopLevelMediaType("image") => new ImageContentBlock
            {
                Data = EncodingUtilities.GetUtf8Bytes(dataContent.Base64Data.Span),
                MimeType = dataContent.MediaType,
            },

            DataContent dataContent when dataContent.HasTopLevelMediaType("audio") => new AudioContentBlock
            {
                Data = EncodingUtilities.GetUtf8Bytes(dataContent.Base64Data.Span),
                MimeType = dataContent.MediaType,
            },

            DataContent dataContent => new EmbeddedResourceBlock
            {
                Resource = new BlobResourceContents
                {
                    Blob = EncodingUtilities.GetUtf8Bytes(dataContent.Base64Data.Span),
                    MimeType = dataContent.MediaType,
                    Uri = string.Empty,
                }
            },

            FunctionCallContent callContent => new ToolUseContentBlock()
            {
                Id = callContent.CallId,
                Name = callContent.Name,
                Input = JsonSerializer.SerializeToElement(callContent.Arguments, options.GetTypeInfo<IDictionary<string, object?>>()!),
            },

            FunctionResultContent resultContent => new ToolResultContentBlock()
            {
                ToolUseId = resultContent.CallId,
                IsError = resultContent.Exception is not null,
                Content =
                    resultContent.Result is AIContent c ? [c.ToContentBlock(options)] :
                    resultContent.Result is IEnumerable<AIContent> ec ? [.. ec.Select(c => c.ToContentBlock(options))] :
                    [new TextContentBlock { Text = JsonSerializer.Serialize(content, options.GetTypeInfo<object>()) }],
                StructuredContent = resultContent.Result is JsonElement je ? je : null,
            },

            _ => new TextContentBlock
            {
                Text = JsonSerializer.Serialize(content, options.GetTypeInfo(typeof(object))),
            }
        };

        contentBlock.Meta = content.AdditionalProperties?.ToJsonObject(options);

        return contentBlock;
    }

    private sealed class ToolAIFunctionDeclaration(Tool tool) : AIFunctionDeclaration
    {
        public override string Name => tool.Name;

        public override string Description => tool.Description ?? "";

        public override IReadOnlyDictionary<string, object?> AdditionalProperties =>
            field ??= tool.Meta is { } meta ? meta.ToDictionary(p => p.Key, p => (object?)p.Value) : [];

        public override JsonElement JsonSchema => tool.InputSchema;

        public override JsonElement? ReturnJsonSchema => tool.OutputSchema;

        public override object? GetService(Type serviceType, object? serviceKey = null)
        {
            Throw.IfNull(serviceType);

            return
                serviceKey is null && serviceType.IsInstanceOfType(tool) ? tool :
                base.GetService(serviceType, serviceKey);
        }
    }
}
