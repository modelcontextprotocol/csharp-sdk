using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents an instance of a Model Context Protocol (MCP) server that connects to and communicates with an MCP client.
/// </summary>
#pragma warning disable CS0618 // Type or member is obsolete
public abstract partial class McpServer : McpSession, IMcpServer
#pragma warning restore CS0618 // Type or member is obsolete
{
    /// <summary>
    /// Caches request schemas for elicitation requests based on the type and serializer options.
    /// </summary>
    private static readonly ConditionalWeakTable<JsonSerializerOptions, ConcurrentDictionary<Type, ElicitRequestParams.RequestSchema>> s_elicitResultSchemaCache = new();

    private static Dictionary<string, HashSet<string>>? s_elicitAllowedProperties = null;

    /// <summary>
    /// Creates a new instance of an <see cref="McpServer"/>.
    /// </summary>
    /// <param name="transport">Transport to use for the server representing an already-established MCP session.</param>
    /// <param name="serverOptions">Configuration options for this server, including capabilities. </param>
    /// <param name="loggerFactory">Logger factory to use for logging. If null, logging will be disabled.</param>
    /// <param name="serviceProvider">Optional service provider to create new instances of tools and other dependencies.</param>
    /// <returns>An <see cref="McpServer"/> instance that should be disposed when no longer needed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="transport"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="serverOptions"/> is <see langword="null"/>.</exception>
    public static McpServer Create(
        ITransport transport,
        McpServerOptions serverOptions,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? serviceProvider = null)
    {
        Throw.IfNull(transport);
        Throw.IfNull(serverOptions);

        return new McpServerImpl(transport, serverOptions, loggerFactory, serviceProvider);
    }

    /// <summary>
    /// Requests to sample an LLM via the client using the specified request parameters.
    /// </summary>
    /// <param name="request">The parameters for the sampling request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task containing the sampling result from the client.</returns>
    /// <exception cref="InvalidOperationException">The client does not support sampling.</exception>
    public ValueTask<CreateMessageResult> SampleAsync(
        CreateMessageRequestParams request, CancellationToken cancellationToken = default)
    {
        ThrowIfSamplingUnsupported();

        return SendRequestAsync(
            RequestMethods.SamplingCreateMessage,
            request,
            McpJsonUtilities.JsonContext.Default.CreateMessageRequestParams,
            McpJsonUtilities.JsonContext.Default.CreateMessageResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Requests to sample an LLM via the client using the provided chat messages and options.
    /// </summary>
    /// <param name="messages">The messages to send as part of the request.</param>
    /// <param name="options">The options to use for the request, including model parameters and constraints.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task containing the chat response from the model.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="messages"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The client does not support sampling.</exception>
    public async Task<ChatResponse> SampleAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = default, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(messages);

        StringBuilder? systemPrompt = null;

        if (options?.Instructions is { } instructions)
        {
            (systemPrompt ??= new()).Append(instructions);
        }

        List<SamplingMessage> samplingMessages = [];
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                if (systemPrompt is null)
                {
                    systemPrompt = new();
                }
                else
                {
                    systemPrompt.AppendLine();
                }

                systemPrompt.Append(message.Text);
                continue;
            }

            if (message.Role == ChatRole.User || message.Role == ChatRole.Assistant)
            {
                Role role = message.Role == ChatRole.User ? Role.User : Role.Assistant;

                foreach (var content in message.Contents)
                {
                    switch (content)
                    {
                        case TextContent textContent:
                            samplingMessages.Add(new()
                            {
                                Role = role,
                                Content = new TextContentBlock { Text = textContent.Text },
                            });
                            break;

                        case DataContent dataContent when dataContent.HasTopLevelMediaType("image") || dataContent.HasTopLevelMediaType("audio"):
                            samplingMessages.Add(new()
                            {
                                Role = role,
                                Content = dataContent.HasTopLevelMediaType("image") ?
                                    new ImageContentBlock
                                    {
                                        MimeType = dataContent.MediaType,
                                        Data = dataContent.Base64Data.ToString(),
                                    } :
                                    new AudioContentBlock
                                    {
                                        MimeType = dataContent.MediaType,
                                        Data = dataContent.Base64Data.ToString(),
                                    },
                            });
                            break;
                    }
                }
            }
        }

        ModelPreferences? modelPreferences = null;
        if (options?.ModelId is { } modelId)
        {
            modelPreferences = new() { Hints = [new() { Name = modelId }] };
        }

        var result = await SampleAsync(new()
        {
            Messages = samplingMessages,
            MaxTokens = options?.MaxOutputTokens,
            StopSequences = options?.StopSequences?.ToArray(),
            SystemPrompt = systemPrompt?.ToString(),
            Temperature = options?.Temperature,
            ModelPreferences = modelPreferences,
        }, cancellationToken).ConfigureAwait(false);

        AIContent? responseContent = result.Content.ToAIContent();

        return new(new ChatMessage(result.Role is Role.User ? ChatRole.User : ChatRole.Assistant, responseContent is not null ? [responseContent] : []))
        {
            ModelId = result.Model,
            FinishReason = result.StopReason switch
            {
                "maxTokens" => ChatFinishReason.Length,
                "endTurn" or "stopSequence" or _ => ChatFinishReason.Stop,
            }
        };
    }

    /// <summary>
    /// Creates an <see cref="IChatClient"/> wrapper that can be used to send sampling requests to the client.
    /// </summary>
    /// <returns>The <see cref="IChatClient"/> that can be used to issue sampling requests to the client.</returns>
    /// <exception cref="InvalidOperationException">The client does not support sampling.</exception>
    public IChatClient AsSamplingChatClient()
    {
        ThrowIfSamplingUnsupported();
        return new SamplingChatClient(this);
    }

    /// <summary>Gets an <see cref="ILogger"/> on which logged messages will be sent as notifications to the client.</summary>
    /// <returns>An <see cref="ILogger"/> that can be used to log to the client..</returns>
    public ILoggerProvider AsClientLoggerProvider()
    {
        return new ClientLoggerProvider(this);
    }

    /// <summary>
    /// Requests the client to list the roots it exposes.
    /// </summary>
    /// <param name="request">The parameters for the list roots request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task containing the list of roots exposed by the client.</returns>
    /// <exception cref="InvalidOperationException">The client does not support roots.</exception>
    public ValueTask<ListRootsResult> RequestRootsAsync(
        ListRootsRequestParams request, CancellationToken cancellationToken = default)
    {
        ThrowIfRootsUnsupported();

        return SendRequestAsync(
            RequestMethods.RootsList,
            request,
            McpJsonUtilities.JsonContext.Default.ListRootsRequestParams,
            McpJsonUtilities.JsonContext.Default.ListRootsResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Requests additional information from the user via the client, allowing the server to elicit structured data.
    /// </summary>
    /// <param name="request">The parameters for the elicitation request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task containing the elicitation result.</returns>
    /// <exception cref="InvalidOperationException">The client does not support elicitation.</exception>
    public ValueTask<ElicitResult> ElicitAsync(
        ElicitRequestParams request, CancellationToken cancellationToken = default)
    {
        ThrowIfElicitationUnsupported();

        return SendRequestAsync(
            RequestMethods.ElicitationCreate,
            request,
            McpJsonUtilities.JsonContext.Default.ElicitRequestParams,
            McpJsonUtilities.JsonContext.Default.ElicitResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Requests additional information from the user via the client, constructing a request schema from the
    /// public serializable properties of <typeparamref name="T"/> and deserializing the response into <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type describing the expected input shape. Only primitive members are supported (string, number, boolean, enum).</typeparam>
    /// <param name="message">The message to present to the user.</param>
    /// <param name="serializerOptions">Serializer options that influence property naming and deserialization.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>An <see cref="ElicitResult{T}"/> with the user's response, if accepted.</returns>
    /// <remarks>
    /// Elicitation uses a constrained subset of JSON Schema and only supports strings, numbers/integers, booleans and string enums.
    /// Unsupported member types are ignored when constructing the schema.
    /// </remarks>
    public async ValueTask<ElicitResult<T>> ElicitAsync<T>(
        string message,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfElicitationUnsupported();

        serializerOptions ??= McpJsonUtilities.DefaultOptions;
        serializerOptions.MakeReadOnly();

        var dict = s_elicitResultSchemaCache.GetValue(serializerOptions, _ => new());

#if NET
        var schema = dict.GetOrAdd(typeof(T), static (t, s) => BuildRequestSchema(t, s), serializerOptions);
#else
        var schema = dict.GetOrAdd(typeof(T), type => BuildRequestSchema(type, serializerOptions));
#endif

        var request = new ElicitRequestParams
        {
            Message = message,
            RequestedSchema = schema,
        };

        var raw = await ElicitAsync(request, cancellationToken).ConfigureAwait(false);

        if (!raw.IsAccepted || raw.Content is null)
        {
            return new ElicitResult<T> { Action = raw.Action, Content = default };
        }

        var obj = new JsonObject();
        foreach (var kvp in raw.Content)
        {
            obj[kvp.Key] = JsonNode.Parse(kvp.Value.GetRawText());
        }

        T? typed = JsonSerializer.Deserialize(obj, serializerOptions.GetTypeInfo<T>());
        return new ElicitResult<T> { Action = raw.Action, Content = typed };
    }

    /// <summary>
    /// Builds a request schema for elicitation based on the public serializable properties of <paramref name="type"/>.
    /// </summary>
    /// <param name="type">The type of the schema being built.</param>
    /// <param name="serializerOptions">The serializer options to use.</param>
    /// <returns>The built request schema.</returns>
    /// <exception cref="McpException"></exception>
    private static ElicitRequestParams.RequestSchema BuildRequestSchema(Type type, JsonSerializerOptions serializerOptions)
    {
        var schema = new ElicitRequestParams.RequestSchema();
        var props = schema.Properties;

        JsonTypeInfo typeInfo = serializerOptions.GetTypeInfo(type);

        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            throw new McpException($"Type '{type.FullName}' is not supported for elicitation requests.");
        }

        foreach (JsonPropertyInfo pi in typeInfo.Properties)
        {
            var def = CreatePrimitiveSchema(pi.PropertyType, serializerOptions);
            props[pi.Name] = def;
        }

        return schema;
    }

    /// <summary>
    /// Creates a primitive schema definition for the specified type, if supported.
    /// </summary>
    /// <param name="type">The type to create the schema for.</param>
    /// <param name="serializerOptions">The serializer options to use.</param>
    /// <returns>The created primitive schema definition.</returns>
    /// <exception cref="McpException">Thrown when the type is not supported.</exception>
    private static ElicitRequestParams.PrimitiveSchemaDefinition CreatePrimitiveSchema(Type type, JsonSerializerOptions serializerOptions)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            throw new McpException($"Type '{type.FullName}' is not a supported property type for elicitation requests. Nullable types are not supported.");
        }

        var typeInfo = serializerOptions.GetTypeInfo(type);

        if (typeInfo.Kind != JsonTypeInfoKind.None)
        {
            throw new McpException($"Type '{type.FullName}' is not a supported property type for elicitation requests.");
        }

        var jsonElement = AIJsonUtilities.CreateJsonSchema(type, serializerOptions: serializerOptions);

        if (!TryValidateElicitationPrimitiveSchema(jsonElement, type, out var error))
        {
            throw new McpException(error);
        }

        var primitiveSchemaDefinition =
            jsonElement.Deserialize(McpJsonUtilities.JsonContext.Default.PrimitiveSchemaDefinition);

        if (primitiveSchemaDefinition is null)
            throw new McpException($"Type '{type.FullName}' is not a supported property type for elicitation requests.");

        return primitiveSchemaDefinition;
    }

    /// <summary>
    /// Validate the produced schema strictly to the subset we support. We only accept an object schema
    /// with a supported primitive type keyword and no additional unsupported keywords.Reject things like
    /// {}, 'true', or schemas that include unrelated keywords(e.g.items, properties, patternProperties, etc.).
    /// </summary>
    /// <param name="schema">The schema to validate.</param>
    /// <param name="type">The type of the schema being validated, just for reporting errors.</param>
    /// <param name="error">The error message, if validation fails.</param>
    /// <returns></returns>
    private static bool TryValidateElicitationPrimitiveSchema(JsonElement schema, Type type,
        [NotNullWhen(false)] out string? error)
    {
        if (schema.ValueKind is not JsonValueKind.Object)
        {
            error = $"Schema generated for type '{type.FullName}' is invalid: expected an object schema.";
            return false;
        }

        if (!schema.TryGetProperty("type", out JsonElement typeProperty)
            || typeProperty.ValueKind is not JsonValueKind.String)
        {
            error = $"Schema generated for type '{type.FullName}' is invalid: missing or invalid 'type' keyword.";
            return false;
        }

        var typeKeyword = typeProperty.GetString();

        if (string.IsNullOrEmpty(typeKeyword))
        {
            error = $"Schema generated for type '{type.FullName}' is invalid: empty 'type' value.";
            return false;
        }

        if (typeKeyword is not ("string" or "number" or "integer" or "boolean"))
        {
            error = $"Schema generated for type '{type.FullName}' is invalid: unsupported primitive type '{typeKeyword}'.";
            return false;
        }

        s_elicitAllowedProperties ??= new()
        {
            ["string"] = ["type", "title", "description", "minLength", "maxLength", "format", "enum", "enumNames"],
            ["number"] = ["type", "title", "description", "minimum", "maximum"],
            ["integer"] = ["type", "title", "description", "minimum", "maximum"],
            ["boolean"] = ["type", "title", "description", "default"]
        };

        var allowed = s_elicitAllowedProperties[typeKeyword];

        foreach (JsonProperty prop in schema.EnumerateObject())
        {
            if (!allowed.Contains(prop.Name))
            {
                error = $"The property '{type.FullName}.{prop.Name}' is not supported for elicitation.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private void ThrowIfSamplingUnsupported()
    {
        if (ClientCapabilities?.Sampling is null)
        {
            if (ServerOptions.KnownClientInfo is not null)
            {
                throw new InvalidOperationException("Sampling is not supported in stateless mode.");
            }

            throw new InvalidOperationException("Client does not support sampling.");
        }
    }

    private void ThrowIfRootsUnsupported()
    {
        if (ClientCapabilities?.Roots is null)
        {
            if (ServerOptions.KnownClientInfo is not null)
            {
                throw new InvalidOperationException("Roots are not supported in stateless mode.");
            }

            throw new InvalidOperationException("Client does not support roots.");
        }
    }

    private void ThrowIfElicitationUnsupported()
    {
        if (ClientCapabilities?.Elicitation is null)
        {
            if (ServerOptions.KnownClientInfo is not null)
            {
                throw new InvalidOperationException("Elicitation is not supported in stateless mode.");
            }

            throw new InvalidOperationException("Client does not support elicitation requests.");
        }
    }

    /// <summary>Provides an <see cref="IChatClient"/> implementation that's implemented via client sampling.</summary>
    private sealed class SamplingChatClient : IChatClient
    {
        private readonly McpServer _server;

        public SamplingChatClient(McpServer server) => _server = server;

        /// <inheritdoc/>
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            _server.SampleAsync(messages, options, cancellationToken);

        /// <inheritdoc/>
        async IAsyncEnumerable<ChatResponseUpdate> IChatClient.GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            foreach (var update in response.ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        /// <inheritdoc/>
        object? IChatClient.GetService(Type serviceType, object? serviceKey)
        {
            Throw.IfNull(serviceType);

            return
                serviceKey is not null ? null :
                serviceType.IsInstanceOfType(this) ? this :
                serviceType.IsInstanceOfType(_server) ? _server :
                null;
        }

        /// <inheritdoc/>
        void IDisposable.Dispose() { } // nop
    }

    /// <summary>
    /// Provides an <see cref="ILoggerProvider"/> implementation for creating loggers
    /// that send logging message notifications to the client for logged messages.
    /// </summary>
    private sealed class ClientLoggerProvider : ILoggerProvider
    {
        private readonly McpServer _server;

        public ClientLoggerProvider(McpServer server) => _server = server;

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName)
        {
            Throw.IfNull(categoryName);

            return new ClientLogger(_server, categoryName);
        }

        /// <inheritdoc />
        void IDisposable.Dispose() { }

        private sealed class ClientLogger : ILogger
        {
            private readonly McpServer _server;
            private readonly string _categoryName;

            public ClientLogger(McpServer server, string categoryName)
            {
                _server = server;
                _categoryName = categoryName;
            }

            /// <inheritdoc />
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
                null;

            /// <inheritdoc />
            public bool IsEnabled(LogLevel logLevel) =>
                _server?.LoggingLevel is { } loggingLevel &&
                McpServerImpl.ToLoggingLevel(logLevel) >= loggingLevel;

            /// <inheritdoc />
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                Throw.IfNull(formatter);

                LogInternal(logLevel, formatter(state, exception));

                void LogInternal(LogLevel level, string message)
                {
                    _ = _server.SendNotificationAsync(NotificationMethods.LoggingMessageNotification, new LoggingMessageNotificationParams
                    {
                        Level = McpServerImpl.ToLoggingLevel(level),
                        Data = JsonSerializer.SerializeToElement(message, McpJsonUtilities.JsonContext.Default.String),
                        Logger = _categoryName,
                    });
                }
            }
        }
    }
}
