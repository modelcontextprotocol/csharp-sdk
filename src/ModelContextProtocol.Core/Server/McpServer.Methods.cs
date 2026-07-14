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
public abstract partial class McpServer : McpSession
{
    /// <summary>
    /// Caches request schemas for elicitation requests based on the type and serializer options.
    /// </summary>
    private static readonly ConditionalWeakTable<JsonSerializerOptions, ConcurrentDictionary<Type, ElicitRequestParams.RequestSchema>> s_elicitResultSchemaCache = new();

    private static Dictionary<string, HashSet<string>>? s_elicitAllowedProperties = null;

    /// <summary>
    /// Creates a new instance of an <see cref="McpServer"/>.
    /// </summary>
    /// <param name="transport">The transport to use for the server representing an already-established MCP session.</param>
    /// <param name="serverOptions">Configuration options for this server, including capabilities. </param>
    /// <param name="loggerFactory">Logger factory to use for logging. If null, logging will be disabled.</param>
    /// <param name="serviceProvider">Optional service provider to create new instances of tools and other dependencies.</param>
    /// <returns>An <see cref="McpServer"/> instance that should be disposed when no longer needed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="transport"/> or <paramref name="serverOptions"/> is <see langword="null"/>.</exception>
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
    /// <param name="requestParams">The parameters for the sampling request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task containing the sampling result from the client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The client does not support sampling.</exception>
    /// <exception cref="McpException">The request failed or the client returned an error response.</exception>
    /// <remarks>
    /// <para>
    /// When the server is using the Streamable HTTP transport, prefer calling this method on the
    /// <see cref="McpServer"/> instance available via <c>RequestContext</c> from inside a tool, prompt,
    /// or resource handler. That routes the request through the originating POST response stream via
    /// <see cref="JsonRpcMessageContext.RelatedTransport"/>, which is always open for the duration of
    /// the request, rather than relying on the optional standalone GET SSE stream.
    /// </para>
    /// <para>
    /// When called during task-augmented tool execution, this method automatically updates the task
    /// status to <see cref="McpTaskStatus.InputRequired"/> while waiting for the client response,
    /// then returns to <see cref="McpTaskStatus.Working"/> when the response is received.
    /// </para>
    /// </remarks>
    [Obsolete(Obsoletions.DeprecatedSampling_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
    public ValueTask<CreateMessageResult> SampleAsync(
        CreateMessageRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);

        // If executing inside a background task, redirect sampling through the task store.
        // Capability checks (ThrowIfSamplingUnsupported) are intentionally skipped here because the
        // client opted into the tasks extension when submitting the originating request, and input
        // requests are delivered through the tasks/get response channel rather than as direct
        // server->client requests. See SendRequestViaTaskAsync remarks.
        if (McpTaskExecutionContext.Current.Value is { } taskContext)
        {
            return SendRequestViaTaskAsync(taskContext, RequestMethods.SamplingCreateMessage, requestParams,
                McpJsonUtilities.JsonContext.Default.CreateMessageRequestParams,
                McpJsonUtilities.JsonContext.Default.CreateMessageResult,
                cancellationToken);
        }

        ThrowIfSamplingUnsupported();

        return SendRequestAsync(
            RequestMethods.SamplingCreateMessage,
            requestParams,
            McpJsonUtilities.JsonContext.Default.CreateMessageRequestParams,
            McpJsonUtilities.JsonContext.Default.CreateMessageResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Requests to sample an LLM via the client using the provided chat messages and options.
    /// </summary>
    /// <param name="messages">The messages to send as part of the request.</param>
    /// <param name="chatOptions">The options to use for the request, including model parameters and constraints.</param>
    /// <param name="serializerOptions">The <see cref="JsonSerializerOptions"/> to use for serializing user-provided objects. If <see langword="null"/>, <see cref="McpJsonUtilities.DefaultOptions"/> is used.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task containing the chat response from the model.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="messages"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The client does not support sampling.</exception>
    /// <exception cref="McpException">The request failed or the client returned an error response.</exception>
    [Obsolete(Obsoletions.DeprecatedSampling_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
    public async Task<ChatResponse> SampleAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? chatOptions = default, JsonSerializerOptions? serializerOptions = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(messages);

        serializerOptions ??= McpJsonUtilities.DefaultOptions;

        StringBuilder? systemPrompt = null;

        if (chatOptions?.Instructions is { } instructions)
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

            Role role = message.Role == ChatRole.Assistant ? Role.Assistant : Role.User;

            // Group all content blocks from this message into a single SamplingMessage
            List<ContentBlock> contentBlocks = [];
            foreach (var content in message.Contents)
            {
                if (content.ToContentBlock() is { } contentBlock)
                {
                    contentBlocks.Add(contentBlock);
                }
            }

            if (contentBlocks.Count > 0)
            {
                samplingMessages.Add(new()
                {
                    Role = role,
                    Content = contentBlocks,
                });
            }
        }

        ModelPreferences? modelPreferences = null;
        if (chatOptions?.ModelId is { } modelId)
        {
            modelPreferences = new() { Hints = [new() { Name = modelId }] };
        }

        IList<Tool>? tools = null;
        if (chatOptions?.Tools is { Count: > 0 })
        {
            foreach (var tool in chatOptions.Tools)
            {
                if (tool is AIFunctionDeclaration af)
                {
                    (tools ??= []).Add(new()
                    {
                        Name = af.Name,
                        Description = af.Description,
                        InputSchema = af.JsonSchema,
                        Meta = af.AdditionalProperties.ToJsonObject(serializerOptions),
                    });
                }
            }
        }

        ToolChoice? toolChoice = chatOptions?.ToolMode switch
        {
            NoneChatToolMode => new() { Mode = ToolChoice.ModeNone },
            AutoChatToolMode => new() { Mode = ToolChoice.ModeAuto },
            RequiredChatToolMode => new() { Mode = ToolChoice.ModeRequired },
            _ => null,
        };

        var result = await SampleAsync(new CreateMessageRequestParams
        {
            MaxTokens = chatOptions?.MaxOutputTokens ?? ServerOptions.MaxSamplingOutputTokens,
            Messages = samplingMessages,
            ModelPreferences = modelPreferences,
            StopSequences = chatOptions?.StopSequences?.ToArray(),
            SystemPrompt = systemPrompt?.ToString(),
            Temperature = chatOptions?.Temperature,
            ToolChoice = toolChoice,
            Tools = tools,
            Meta = chatOptions?.AdditionalProperties?.ToJsonObject(serializerOptions),
        }, cancellationToken).ConfigureAwait(false);

        List<AIContent> responseContents = [];
        foreach (var block in result.Content)
        {
            if (block.ToAIContent(serializerOptions) is { } content)
            {
                responseContents.Add(content);
            }
        }

        return new(new ChatMessage(result.Role is Role.User ? ChatRole.User : ChatRole.Assistant, responseContents))
        {
            CreatedAt = DateTimeOffset.UtcNow,
            FinishReason = result.StopReason switch
            {
                CreateMessageResult.StopReasonEndTurn => ChatFinishReason.Stop,
                CreateMessageResult.StopReasonMaxTokens => ChatFinishReason.Length,
                CreateMessageResult.StopReasonStopSequence => ChatFinishReason.Stop,
                CreateMessageResult.StopReasonToolUse => ChatFinishReason.ToolCalls,
                _ => null,
            },
            ModelId = result.Model,
        };
    }

    /// <summary>
    /// Creates an <see cref="IChatClient"/> wrapper that can be used to send sampling requests to the client.
    /// </summary>
    /// <param name="serializerOptions">The <see cref="JsonSerializerOptions"/> to use for serialization. If <see langword="null"/>, <see cref="McpJsonUtilities.DefaultOptions"/> is used.</param>
    /// <returns>The <see cref="IChatClient"/> that can be used to issue sampling requests to the client.</returns>
    /// <exception cref="InvalidOperationException">The client does not support sampling.</exception>
    /// <remarks>
    /// When the server is using the Streamable HTTP transport, prefer obtaining this chat client from the
    /// <see cref="McpServer"/> instance available via <c>RequestContext</c> from inside a tool, prompt,
    /// or resource handler. That routes sampling requests through the originating POST response stream via
    /// <see cref="JsonRpcMessageContext.RelatedTransport"/>, which is always open for the duration of
    /// the request, rather than relying on the optional standalone GET SSE stream.
    /// </remarks>
    [Obsolete(Obsoletions.DeprecatedSampling_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
    public IChatClient AsSamplingChatClient(JsonSerializerOptions? serializerOptions = null)
    {
        ThrowIfSamplingUnsupported();

        return new SamplingChatClient(this, serializerOptions ?? McpJsonUtilities.DefaultOptions);
    }

    /// <summary>Gets an <see cref="ILogger"/> on which logged messages will be sent as notifications to the client.</summary>
    /// <returns>An <see cref="ILogger"/> that can be used to log to the client.</returns>
    [Obsolete(Obsoletions.DeprecatedLogging_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
    public ILoggerProvider AsClientLoggerProvider() => 
        new ClientLoggerProvider(this);

    /// <summary>
    /// Requests the client to list the roots it exposes.
    /// </summary>
    /// <param name="requestParams">The parameters for the list roots request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task containing the list of roots exposed by the client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The client does not support roots.</exception>
    /// <exception cref="McpException">The request failed or the client returned an error response.</exception>
    /// <remarks>
    /// When the server is using the Streamable HTTP transport, prefer calling this method on the
    /// <see cref="McpServer"/> instance available via <c>RequestContext</c> from inside a tool, prompt,
    /// or resource handler. That routes the request through the originating POST response stream via
    /// <see cref="JsonRpcMessageContext.RelatedTransport"/>, which is always open for the duration of
    /// the request, rather than relying on the optional standalone GET SSE stream.
    /// </remarks>
    [Obsolete(Obsoletions.DeprecatedRoots_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
    public ValueTask<ListRootsResult> RequestRootsAsync(
        ListRootsRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);

        // If executing inside a background task, redirect through the task store.
        // Capability checks (ThrowIfRootsUnsupported) are intentionally skipped here because the
        // client opted into the tasks extension when submitting the originating request, and input
        // requests are delivered through the tasks/get response channel rather than as direct
        // server->client requests. See SendRequestViaTaskAsync remarks.
        if (McpTaskExecutionContext.Current.Value is { } taskContext)
        {
            return SendRequestViaTaskAsync(taskContext, RequestMethods.RootsList, requestParams,
                McpJsonUtilities.JsonContext.Default.ListRootsRequestParams,
                McpJsonUtilities.JsonContext.Default.ListRootsResult,
                cancellationToken);
        }

        ThrowIfRootsUnsupported();

        return SendRequestAsync(
            RequestMethods.RootsList,
            requestParams,
            McpJsonUtilities.JsonContext.Default.ListRootsRequestParams,
            McpJsonUtilities.JsonContext.Default.ListRootsResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Requests additional information from the user via the client, allowing the server to elicit structured data.
    /// </summary>
    /// <param name="requestParams">The parameters for the elicitation request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A task containing the elicitation result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The client does not support elicitation.</exception>
    /// <exception cref="McpException">The request failed or the client returned an error response.</exception>
    /// <remarks>
    /// <para>
    /// When the server is using the Streamable HTTP transport, prefer calling this method on the
    /// <see cref="McpServer"/> instance available via <c>RequestContext</c> from inside a tool, prompt,
    /// or resource handler. That routes the request through the originating POST response stream via
    /// <see cref="JsonRpcMessageContext.RelatedTransport"/>, which is always open for the duration of
    /// the request, rather than relying on the optional standalone GET SSE stream.
    /// </para>
    /// <para>
    /// When called during task-augmented tool execution, this method automatically updates the task
    /// status to <see cref="McpTaskStatus.InputRequired"/> while waiting for user input,
    /// then returns to <see cref="McpTaskStatus.Working"/> when the response is received.
    /// </para>
    /// </remarks>
    public async ValueTask<ElicitResult> ElicitAsync(
        ElicitRequestParams requestParams, 
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);

        // If executing inside a background task, redirect elicitation through the task store.
        // Capability checks (ThrowIfElicitationUnsupported) are intentionally skipped here because
        // the client opted into the tasks extension when submitting the originating request, and
        // input requests are delivered through the tasks/get response channel rather than as
        // direct server->client requests. See SendRequestViaTaskAsync remarks.
        if (McpTaskExecutionContext.Current.Value is { } taskContext)
        {
            var taskResult = await SendRequestViaTaskAsync(taskContext, RequestMethods.ElicitationCreate, requestParams,
                McpJsonUtilities.JsonContext.Default.ElicitRequestParams,
                McpJsonUtilities.JsonContext.Default.ElicitResult,
                cancellationToken).ConfigureAwait(false);
            return taskResult ?? new ElicitResult { Action = "cancel" };
        }

        ThrowIfElicitationUnsupported(requestParams);

        var result = await SendRequestAsync(
            RequestMethods.ElicitationCreate,
            requestParams,
            McpJsonUtilities.JsonContext.Default.ElicitRequestParams,
            McpJsonUtilities.JsonContext.Default.ElicitResult,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return ElicitResult.WithDefaults(requestParams, result);
    }

    /// <summary>
    /// Requests additional information from the user via the client, constructing a request schema from the
    /// public serializable properties of <typeparamref name="T"/> and deserializing the response into <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type describing the expected input shape. Only primitive members are supported (string, number, boolean, enum).</typeparam>
    /// <param name="message">The message to present to the user.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>An <see cref="ElicitResult{T}"/> with the user's response, if accepted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="message"/> is empty or composed entirely of whitespace.</exception>
    /// <exception cref="InvalidOperationException">The client does not support elicitation.</exception>
    /// <exception cref="McpException">The request failed or the client returned an error response.</exception>
    /// <remarks>
    /// Elicitation uses a constrained subset of JSON Schema and only supports strings, numbers/integers, booleans and string enums.
    /// Unsupported member types are ignored when constructing the schema.
    /// </remarks>
    public async ValueTask<ElicitResult<T>> ElicitAsync<T>(
        string message,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(message);

        var serializerOptions = options?.JsonSerializerOptions ?? McpJsonUtilities.DefaultOptions;
        serializerOptions.MakeReadOnly();

        var dict = s_elicitResultSchemaCache.GetValue(serializerOptions, _ => new());

        var schema = dict.GetOrAdd(typeof(T),
#if NET
            static (t, s) => BuildRequestSchema(t, s), serializerOptions);
#else
            type => BuildRequestSchema(type, serializerOptions));
#endif

        var request = new ElicitRequestParams
        {
            Message = message,
            RequestedSchema = schema,
            Meta = options?.GetMetaForRequest(),
        };

        ThrowIfElicitationUnsupported(request);

        var raw = await ElicitAsync(request, cancellationToken).ConfigureAwait(false);

        if (!raw.IsAccepted || raw.Content is null)
        {
            return new ElicitResult<T> { Action = raw.Action, Content = default };
        }

        JsonObject obj = [];
        foreach (var kvp in raw.Content)
        {
            obj[kvp.Key] = JsonNode.Parse(kvp.Value.GetRawText());
        }

        T? typed = JsonSerializer.Deserialize(obj, serializerOptions.GetTypeInfo<T>());
        return new ElicitResult<T> { Action = raw.Action, Content = typed };
    }

    /// <summary>
    /// Sends a task status notification to the connected client.
    /// </summary>
    /// <param name="notificationParams">The task status notification parameters to send.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="notificationParams"/> is <see langword="null"/>.</exception>
    public Task SendTaskStatusNotificationAsync(
        TaskStatusNotificationParams notificationParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(notificationParams);

        return SendNotificationAsync(
            NotificationMethods.TaskStatusNotification,
            notificationParams,
            McpJsonUtilities.JsonContext.Default.TaskStatusNotificationParams,
            cancellationToken);
    }

    /// <summary>
    /// Builds a request schema for elicitation based on the public serializable properties of <paramref name="type"/>.
    /// </summary>
    /// <param name="type">The type of the schema being built.</param>
    /// <param name="serializerOptions">The serializer options to use.</param>
    /// <returns>The built request schema.</returns>
    /// <exception cref="McpProtocolException"></exception>
    private static ElicitRequestParams.RequestSchema BuildRequestSchema(Type type, JsonSerializerOptions serializerOptions)
    {
        var schema = new ElicitRequestParams.RequestSchema();
        var props = schema.Properties;

        JsonTypeInfo typeInfo = serializerOptions.GetTypeInfo(type);

        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            throw new McpProtocolException($"Type '{type.FullName}' is not supported for elicitation requests.");
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
    /// <exception cref="McpProtocolException">The type is not supported.</exception>
    private static ElicitRequestParams.PrimitiveSchemaDefinition CreatePrimitiveSchema(Type type, JsonSerializerOptions serializerOptions)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            throw new McpProtocolException($"Type '{type.FullName}' is not a supported property type for elicitation requests. Nullable types are not supported.");
        }

        var typeInfo = serializerOptions.GetTypeInfo(type);

        if (typeInfo.Kind != JsonTypeInfoKind.None)
        {
            throw new McpProtocolException($"Type '{type.FullName}' is not a supported property type for elicitation requests.");
        }

        var jsonElement = AIJsonUtilities.CreateJsonSchema(type, serializerOptions: serializerOptions);

        if (!TryValidateElicitationPrimitiveSchema(jsonElement, type, out var error))
        {
            throw new McpProtocolException(error);
        }

        return
            jsonElement.Deserialize(McpJsonUtilities.JsonContext.Default.PrimitiveSchemaDefinition) ??
            throw new McpProtocolException($"Type '{type.FullName}' is not a supported property type for elicitation requests.");
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
            if (ClientCapabilities is null)
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
            if (ClientCapabilities is null)
            {
                throw new InvalidOperationException("Roots are not supported in stateless mode.");
            }

            throw new InvalidOperationException("Client does not support roots.");
        }
    }

    /// <summary>
    /// Creates a scope that redirects server-initiated requests (elicitation, sampling, list roots) through
    /// the task store as input requests for the duration of the scope. Use this when executing tool logic
    /// in the background as a task, so that any server-to-client requests are surfaced to the client via
    /// the task's <see cref="McpTaskStatus.InputRequired"/> state instead of direct JSON-RPC messages.
    /// </summary>
    /// <param name="taskId">The task ID in the store.</param>
    /// <param name="store">The task store to write input requests to.</param>
    /// <returns>An <see cref="IDisposable"/> that restores the previous context when disposed.</returns>
    public IDisposable CreateMcpTaskScope(
        string taskId,
        IMcpTaskStore store)
    {
        Throw.IfNull(taskId);
        Throw.IfNull(store);

        var previous = McpTaskExecutionContext.Current.Value;
        McpTaskExecutionContext.Current.Value = new McpTaskExecutionContext
        {
            TaskId = taskId,
            Store = store,
        };
        return new McpTaskExecutionContext.Scope(previous);
    }

    /// <summary>
    /// Sends a server-initiated request through the task store as an input request, then awaits the response.
    /// </summary>
    /// <remarks>
    /// When executing inside a task scope, capability negotiation checks (such as
    /// <see cref="ThrowIfSamplingUnsupported"/>, <see cref="ThrowIfRootsUnsupported"/>, and
    /// <see cref="ThrowIfElicitationUnsupported"/>) are intentionally skipped by the callers
    /// of this helper. The task channel itself is the negotiated capability: the client opted
    /// in to the tasks extension when it submitted the originating request, and is responsible
    /// for handling or rejecting the input requests surfaced through <c>tasks/get</c>.
    /// </remarks>
    private async ValueTask<TResponse> SendRequestViaTaskAsync<TRequest, TResponse>(
        McpTaskExecutionContext taskContext,
        string method,
        TRequest request,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var paramsJson = JsonSerializer.SerializeToElement(request, requestTypeInfo);

        var inputRequest = new InputRequest
        {
            Method = method,
            Params = paramsJson,
        };

        var tcs = new TaskCompletionSource<InputResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        void handler(InputResponseReceivedEventArgs args)
        {
            if (args.TaskId == taskContext.TaskId && args.RequestId == requestId)
            {
                tcs.TrySetResult(args.Response);
            }
        }

        taskContext.Store.InputResponseReceived += handler;
        try
        {
            await taskContext.Store.SetInputRequestsAsync(
                taskContext.TaskId,
                new Dictionary<string, InputRequest> { [requestId] = inputRequest },
                cancellationToken).ConfigureAwait(false);

            var response = await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            return response.Deserialize(responseTypeInfo)!;
        }
        finally
        {
            taskContext.Store.InputResponseReceived -= handler;
        }
    }

    private void ThrowIfElicitationUnsupported(ElicitRequestParams request)
    {
        if (ClientCapabilities is null)
        {
            throw new InvalidOperationException("Elicitation is not supported in stateless mode.");
        }

        var elicitationCapability = ClientCapabilities.Elicitation;
        if (elicitationCapability is null)
        {
            throw new InvalidOperationException("Client does not support elicitation requests.");
        }

        if (string.Equals(request.Mode, "form", StringComparison.Ordinal))
        {
            if (request.RequestedSchema is null)
            {
                throw new ArgumentException("Form mode elicitation requests require a requested schema.");
            }

            if (elicitationCapability.Form is null)
            {
                throw new InvalidOperationException("Client does not support form mode elicitation requests.");
            }
        }
        else if (string.Equals(request.Mode, "url", StringComparison.Ordinal))
        {
            if (request.Url is null)
            {
                throw new ArgumentException("URL mode elicitation requests require a URL.");
            }

            if (request.ElicitationId is null)
            {
                throw new ArgumentException("URL mode elicitation requests require an elicitation ID.");
            }

            if (elicitationCapability.Url is null)
            {
                throw new InvalidOperationException("Client does not support URL mode elicitation requests.");
            }
        }
    }

    /// <summary>Provides an <see cref="IChatClient"/> implementation that's implemented via client sampling.</summary>
    private sealed class SamplingChatClient(McpServer server, JsonSerializerOptions serializerOptions) : IChatClient
    {
        private readonly McpServer _server = server;
        private readonly JsonSerializerOptions _serializerOptions = serializerOptions;

        /// <inheritdoc/>
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? chatOptions = null, CancellationToken cancellationToken = default) =>
            _server.SampleAsync(messages, chatOptions, _serializerOptions, cancellationToken);

        /// <inheritdoc/>
        async IAsyncEnumerable<ChatResponseUpdate> IChatClient.GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? chatOptions, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var response = await GetResponseAsync(messages, chatOptions, cancellationToken).ConfigureAwait(false);
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
    private sealed class ClientLoggerProvider(McpServer server) : ILoggerProvider
    {
        private readonly McpServer _server = server;

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName)
        {
            Throw.IfNull(categoryName);

            return new ClientLogger(_server, categoryName);
        }

        /// <inheritdoc />
        void IDisposable.Dispose() { }

        private sealed class ClientLogger(McpServer server, string categoryName) : ILogger
        {
            private readonly McpServer _server = server;
            private readonly string _categoryName = categoryName;

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
