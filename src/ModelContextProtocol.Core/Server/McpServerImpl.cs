using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Server;

/// <inheritdoc />
#pragma warning disable MCPEXP002
internal sealed partial class McpServerImpl : McpServer
{
    internal static Implementation DefaultImplementation { get; } = new()
    {
        Name = AssemblyNameHelper.DefaultAssemblyName.Name ?? nameof(McpServer),
        Version = AssemblyNameHelper.DefaultAssemblyName.Version?.ToString() ?? "1.0.0",
    };

    private readonly ILogger _logger;
    private readonly ITransport _sessionTransport;
    private readonly bool _servicesScopePerRequest;
    private readonly List<Action> _disposables = [];
    private readonly NotificationHandlers _notificationHandlers;
    private readonly RequestHandlers _requestHandlers;
    private readonly McpSessionHandler _sessionHandler;
    private readonly SemaphoreSlim _disposeLock = new(1, 1);
    private readonly McpTaskCancellationTokenProvider? _taskCancellationTokenProvider;
    private readonly ConcurrentDictionary<string, MrtrContinuation> _mrtrContinuations = new();

    // Track MRTR handler tasks using the same inFlightCount + TCS pattern as
    // McpSessionHandler.ProcessMessagesCoreAsync. Starts at 1 for DisposeAsync itself.
    private int _mrtrInFlightCount = 1;
    private readonly TaskCompletionSource<bool> _allMrtrHandlersCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ClientCapabilities? _clientCapabilities;
    private Implementation? _clientInfo;

    private readonly string _serverOnlyEndpointName;
    private string? _negotiatedProtocolVersion;
    private string _endpointName;
    private int _started;

    private bool _disposed;

    /// <summary>Holds a boxed <see cref="LoggingLevel"/> value for the server.</summary>
    /// <remarks>
    /// Initialized to non-null the first time SetLevel is used. This is stored as a strong box
    /// rather than a nullable to be able to manipulate it atomically.
    /// </remarks>
    private StrongBox<LoggingLevel>? _loggingLevel;

    /// <summary>
    /// Creates a new instance of <see cref="McpServerImpl"/>.
    /// </summary>
    /// <param name="transport">Transport to use for the server representing an already-established session.</param>
    /// <param name="options">Configuration options for this server, including capabilities.
    /// Make sure to accurately reflect exactly what capabilities the server supports and does not support.</param>
    /// <param name="loggerFactory">Logger factory to use for logging</param>
    /// <param name="serviceProvider">Optional service provider to use for dependency injection</param>
    /// <exception cref="McpException">The server was incorrectly configured.</exception>
    public McpServerImpl(ITransport transport, McpServerOptions options, ILoggerFactory? loggerFactory, IServiceProvider? serviceProvider)
#pragma warning restore MCPEXP002
    {
        Throw.IfNull(transport);
        Throw.IfNull(options);

        _sessionTransport = transport;
        ServerOptions = options;
        Services = serviceProvider;
        _serverOnlyEndpointName = $"Server ({options.ServerInfo?.Name ?? DefaultImplementation.Name} {options.ServerInfo?.Version ?? DefaultImplementation.Version})";
        _endpointName = _serverOnlyEndpointName;
        _servicesScopePerRequest = options.ScopeRequests;
        _logger = loggerFactory?.CreateLogger<McpServer>() ?? NullLogger<McpServer>.Instance;

        // Only allocate the cancellation token provider if a task store is configured
        if (options.TaskStore is not null)
        {
            _taskCancellationTokenProvider = new McpTaskCancellationTokenProvider();
        }

        _clientInfo = options.KnownClientInfo;
        _clientCapabilities = options.KnownClientCapabilities;
        UpdateEndpointNameWithClientInfo();

        _notificationHandlers = new();
        _requestHandlers = [];

        // Configure all request handlers based on the supplied options.
        ServerCapabilities = new();
        ConfigureInitialize(options);
        ConfigureTools(options);
        ConfigurePrompts(options);
        ConfigureResources(options);
        ConfigureTasks(options);
        ConfigureLogging(options);
        ConfigureCompletion(options);
        ConfigureExperimentalAndExtensions(options);

        // Wrap MRTR-eligible handlers AFTER all handler registration is complete.
        ConfigureMrtr();

        // Register any notification handlers that were provided.
        if (options.Handlers.NotificationHandlers is { } notificationHandlers)
        {
            _notificationHandlers.RegisterRange(notificationHandlers);
        }

        // Now that everything has been configured, subscribe to any necessary notifications.
        if (transport is not StreamableHttpServerTransport streamableHttpTransport || streamableHttpTransport.Stateless is false)
        {
            Register(ServerOptions.ToolCollection, NotificationMethods.ToolListChangedNotification);
            Register(ServerOptions.PromptCollection, NotificationMethods.PromptListChangedNotification);
            Register(ServerOptions.ResourceCollection, NotificationMethods.ResourceListChangedNotification);

            void Register<TPrimitive>(McpServerPrimitiveCollection<TPrimitive>? collection, string notificationMethod)
                where TPrimitive : IMcpServerPrimitive
            {
                if (collection is not null)
                {
                    EventHandler changed = (sender, e) => _ = this.SendNotificationAsync(notificationMethod);
                    collection.Changed += changed;
                    _disposables.Add(() => collection.Changed -= changed);
                }
            }
        }

        // And initialize the session.
        var incomingMessageFilter = BuildMessageFilterPipeline(options.Filters.Message.IncomingFilters);
        var outgoingMessageFilter = BuildMessageFilterPipeline(options.Filters.Message.OutgoingFilters);
        _sessionHandler = new McpSessionHandler(
            isServer: true,
            _sessionTransport,
            _endpointName!,
            _requestHandlers,
            _notificationHandlers,
            incomingMessageFilter,
            outgoingMessageFilter,
            _logger);
    }

    /// <inheritdoc/>
    public override string? SessionId => _sessionTransport.SessionId;

    /// <inheritdoc/>
    public override string? NegotiatedProtocolVersion => _negotiatedProtocolVersion;

    /// <inheritdoc/>
    public ServerCapabilities ServerCapabilities { get; }

    /// <inheritdoc />
    public override ClientCapabilities? ClientCapabilities => _clientCapabilities;

    /// <inheritdoc />
    public override Implementation? ClientInfo => _clientInfo;

    /// <inheritdoc />
    public override McpServerOptions ServerOptions { get; }

    /// <inheritdoc />
    public override IServiceProvider? Services { get; }

    /// <inheritdoc />
    public override LoggingLevel? LoggingLevel => _loggingLevel?.Value;

    /// <inheritdoc />
    public override async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException($"{nameof(RunAsync)} must only be called once.");
        }

        try
        {
            await _sessionHandler.ProcessMessagesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await DisposeAsync().ConfigureAwait(false);
        }
    }


    /// <inheritdoc/>
    public override Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
        => _sessionHandler.SendRequestAsync(request, cancellationToken);

    /// <inheritdoc/>
    public override Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
        => _sessionHandler.SendMessageAsync(message, cancellationToken);

    /// <inheritdoc/>
    public override IAsyncDisposable RegisterNotificationHandler(string method, Func<JsonRpcNotification, CancellationToken, ValueTask> handler)
        => _sessionHandler.RegisterNotificationHandler(method, handler);

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        using var _ = await _disposeLock.LockAsync().ConfigureAwait(false);

        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Dispose the session handler first — cancels message processing and waits for all
        // in-flight request handlers (including retries in AwaitMrtrHandlerAsync) to complete.
        // After this returns, no new requests can be processed and no new MRTR continuations
        // can be created, so _mrtrContinuations is effectively frozen.
        _taskCancellationTokenProvider?.Dispose();
        _disposables.ForEach(d => d());
        await _sessionHandler.DisposeAsync().ConfigureAwait(false);

        // Cancel all orphaned MRTR handlers still suspended in continuations (waiting for
        // retries that will never arrive now that the session handler is disposed).
        int cancelledCount = _mrtrContinuations.Count;
        foreach (var continuation in _mrtrContinuations.Values)
        {
            continuation.CancelHandler();
        }

        if (cancelledCount > 0)
        {
            MrtrContinuationsCancelled(cancelledCount);
        }

        // Wait for all MRTR handler tasks to complete using the same inFlightCount + TCS
        // pattern as McpSessionHandler.ProcessMessagesCoreAsync. The count started at 1
        // (for DisposeAsync itself); decrementing it here triggers the drain if handlers
        // are still in flight. ObserveHandlerCompletionAsync decrements for each handler.
        if (Interlocked.Decrement(ref _mrtrInFlightCount) != 0)
        {
            await _allMrtrHandlersCompleted.Task.ConfigureAwait(false);
        }
    }

    private void ConfigureInitialize(McpServerOptions options)
    {
        _requestHandlers.Set(RequestMethods.Initialize,
            async (request, _, _) =>
            {
                _clientCapabilities = request?.Capabilities ?? new();
                _clientInfo = request?.ClientInfo;

                // Use the ClientInfo to update the session EndpointName for logging.
                UpdateEndpointNameWithClientInfo();
                _sessionHandler.EndpointName = _endpointName;

                // Negotiate a protocol version. If the server options provide one, use that.
                // Otherwise, try to use whatever the client requested as long as it's supported.
                // If it's not supported, fall back to the latest supported version.
                // Also accept the experimental protocol version when the server has it configured.
                string? protocolVersion = options.ProtocolVersion;
                protocolVersion ??= request?.ProtocolVersion is string clientProtocolVersion &&
                    (McpSessionHandler.SupportedProtocolVersions.Contains(clientProtocolVersion) ||
                     (options.ExperimentalProtocolVersion is not null && clientProtocolVersion == options.ExperimentalProtocolVersion)) ?
                    clientProtocolVersion :
                    McpSessionHandler.LatestProtocolVersion;

                _negotiatedProtocolVersion = protocolVersion;

                // Update session handler with the negotiated protocol version for telemetry
                _sessionHandler.NegotiatedProtocolVersion = protocolVersion;

                return new InitializeResult
                {
                    ProtocolVersion = protocolVersion,
                    Instructions = options.ServerInstructions,
                    ServerInfo = options.ServerInfo ?? DefaultImplementation,
                    Capabilities = ServerCapabilities ?? new(),
                };
            },
            McpJsonUtilities.JsonContext.Default.InitializeRequestParams,
            McpJsonUtilities.JsonContext.Default.InitializeResult);
    }

    private void ConfigureCompletion(McpServerOptions options)
    {
        var completeHandler = options.Handlers.CompleteHandler;
        var completionsCapability = options.Capabilities?.Completions;

        // Build completion value lookups from prompt/resource collections' [AllowedValues]-attributed parameters.
        Dictionary<string, Dictionary<string, string[]>>? promptCompletions = BuildAllowedValueCompletions(options.PromptCollection);
        Dictionary<string, Dictionary<string, string[]>>? resourceCompletions = BuildAllowedValueCompletions(options.ResourceCollection);
        bool hasCollectionCompletions = promptCompletions is not null || resourceCompletions is not null;

        if (completeHandler is null && completionsCapability is null && !hasCollectionCompletions)
        {
            return;
        }

        completeHandler ??= (static async (_, __) => new CompleteResult());

        // Augment the completion handler with allowed values from prompt/resource collections.
        if (hasCollectionCompletions)
        {
            var originalCompleteHandler = completeHandler;
            completeHandler = async (request, cancellationToken) =>
            {
                CompleteResult result = await originalCompleteHandler(request, cancellationToken).ConfigureAwait(false);

                string[]? allowedValues = null;
                switch (request.Params?.Ref)
                {
                    case PromptReference pr when promptCompletions is not null:
                        if (promptCompletions.TryGetValue(pr.Name, out var promptParams))
                        {
                            promptParams.TryGetValue(request.Params.Argument.Name, out allowedValues);
                        }
                        break;

                    case ResourceTemplateReference rtr when resourceCompletions is not null:
                        if (rtr.Uri is not null && resourceCompletions.TryGetValue(rtr.Uri, out var resourceParams))
                        {
                            resourceParams.TryGetValue(request.Params.Argument.Name, out allowedValues);
                        }
                        break;
                }

                if (allowedValues is not null)
                {
                    string partialValue = request.Params!.Argument.Value;
                    foreach (var v in allowedValues)
                    {
                        if (v.StartsWith(partialValue, StringComparison.OrdinalIgnoreCase))
                        {
                            result.Completion.Values.Add(v);
                        }
                    }

                    result.Completion.Total = result.Completion.Values.Count;
                }

                return result;
            };
        }

        completeHandler = BuildFilterPipeline(completeHandler, options.Filters.Request.CompleteFilters);

        ServerCapabilities.Completions = new();

        SetHandler(
            RequestMethods.CompletionComplete,
            completeHandler,
            McpJsonUtilities.JsonContext.Default.CompleteRequestParams,
            McpJsonUtilities.JsonContext.Default.CompleteResult);
    }

    /// <summary>
    /// Builds a lookup of primitive name/URI → (parameter name → allowed values) from the enum values
    /// in the JSON schemas of AIFunction-based prompts or resources.
    /// </summary>
    private static Dictionary<string, Dictionary<string, string[]>>? BuildAllowedValueCompletions<T>(
        McpServerPrimitiveCollection<T>? primitives) where T : class, IMcpServerPrimitive
    {
        if (primitives is null)
        {
            return null;
        }

        Dictionary<string, Dictionary<string, string[]>>? result = null;
        foreach (var primitive in primitives)
        {
            JsonElement schema;
            string id;
            if (primitive is AIFunctionMcpServerPrompt aiPrompt)
            {
                schema = aiPrompt.AIFunction.JsonSchema;
                id = aiPrompt.ProtocolPrompt.Name;
            }
            else if (primitive is AIFunctionMcpServerResource aiResource && aiResource.IsTemplated)
            {
                schema = aiResource.AIFunction.JsonSchema;
                id = aiResource.ProtocolResourceTemplate.UriTemplate;
            }
            else
            {
                continue;
            }

            if (schema.TryGetProperty("properties", out JsonElement properties) &&
                properties.ValueKind is JsonValueKind.Object)
            {
                Dictionary<string, string[]>? paramValues = null;
                foreach (var param in properties.EnumerateObject())
                {
                    if (param.Value.TryGetProperty("enum", out JsonElement enumValues) &&
                        enumValues.ValueKind is JsonValueKind.Array)
                    {
                        List<string>? values = null;
                        foreach (var item in enumValues.EnumerateArray())
                        {
                            if (item.ValueKind is JsonValueKind.String && item.GetString() is { } str)
                            {
                                values ??= [];
                                values.Add(str);
                            }
                        }

                        if (values is not null)
                        {
                            paramValues ??= new(StringComparer.Ordinal);
                            paramValues[param.Name] = [.. values];
                        }
                    }
                }

                if (paramValues is not null)
                {
                    result ??= new(StringComparer.Ordinal);
                    result[id] = paramValues;
                }
            }
        }

        return result;
    }

    private void ConfigureExperimentalAndExtensions(McpServerOptions options)
    {
        ServerCapabilities.Experimental = options.Capabilities?.Experimental;
        ServerCapabilities.Extensions = options.Capabilities?.Extensions;
    }

    private void ConfigureResources(McpServerOptions options)
    {
        var listResourcesHandler = options.Handlers.ListResourcesHandler;
        var listResourceTemplatesHandler = options.Handlers.ListResourceTemplatesHandler;
        var readResourceHandler = options.Handlers.ReadResourceHandler;
        var subscribeHandler = options.Handlers.SubscribeToResourcesHandler;
        var unsubscribeHandler = options.Handlers.UnsubscribeFromResourcesHandler;
        var resources = options.ResourceCollection;
        var resourcesCapability = options.Capabilities?.Resources;

        if (listResourcesHandler is null && listResourceTemplatesHandler is null && readResourceHandler is null &&
            subscribeHandler is null && unsubscribeHandler is null && resources is null &&
            resourcesCapability is null)
        {
            return;
        }

        ServerCapabilities.Resources = new();

        listResourcesHandler ??= (static async (_, __) => new ListResourcesResult());
        listResourceTemplatesHandler ??= (static async (_, __) => new ListResourceTemplatesResult());
        readResourceHandler ??= (static async (request, _) => throw new McpProtocolException($"Unknown resource URI: '{request.Params?.Uri}'", McpErrorCode.ResourceNotFound));
        subscribeHandler ??= (static async (_, __) => new EmptyResult());
        unsubscribeHandler ??= (static async (_, __) => new EmptyResult());
        var listChanged = resourcesCapability?.ListChanged;
        var subscribe = resourcesCapability?.Subscribe;

        // Handle resources provided via DI.
        if (resources is not null)
        {
            var originalListResourcesHandler = listResourcesHandler;
            listResourcesHandler = async (request, cancellationToken) =>
            {
                ListResourcesResult result = originalListResourcesHandler is not null ?
                    await originalListResourcesHandler(request, cancellationToken).ConfigureAwait(false) :
                    new();

                if (request.Params?.Cursor is null)
                {
                    foreach (var r in resources)
                    {
                        if (r.ProtocolResource is { } resource)
                        {
                            result.Resources.Add(resource);
                        }
                    }
                }

                return result;
            };

            var originalListResourceTemplatesHandler = listResourceTemplatesHandler;
            listResourceTemplatesHandler = async (request, cancellationToken) =>
            {
                ListResourceTemplatesResult result = originalListResourceTemplatesHandler is not null ?
                    await originalListResourceTemplatesHandler(request, cancellationToken).ConfigureAwait(false) :
                    new();

                if (request.Params?.Cursor is null)
                {
                    foreach (var rt in resources)
                    {
                        if (rt.IsTemplated)
                        {
                            result.ResourceTemplates.Add(rt.ProtocolResourceTemplate);
                        }
                    }
                }

                return result;
            };

            // Synthesize read resource handler, which covers both resources and resource templates.
            var originalReadResourceHandler = readResourceHandler;
            readResourceHandler = async (request, cancellationToken) =>
            {
                if (request.MatchedPrimitive is McpServerResource matchedResource)
                {
                    return await matchedResource.ReadAsync(request, cancellationToken).ConfigureAwait(false);
                }

                return await originalReadResourceHandler(request, cancellationToken).ConfigureAwait(false);
            };

            listChanged = true;

            // TODO: Implement subscribe/unsubscribe logic for resource and resource template collections.
            // subscribe = true;
        }

        listResourcesHandler = BuildFilterPipeline(listResourcesHandler, options.Filters.Request.ListResourcesFilters);
        listResourceTemplatesHandler = BuildFilterPipeline(listResourceTemplatesHandler, options.Filters.Request.ListResourceTemplatesFilters);
        readResourceHandler = BuildFilterPipeline(readResourceHandler, options.Filters.Request.ReadResourceFilters, handler =>
            async (request, cancellationToken) =>
            {
                // Initial handler that sets MatchedPrimitive
                if (request.Params?.Uri is { } uri && resources is not null)
                {
                    // First try an O(1) lookup by exact match.
                    if (resources.TryGetPrimitive(uri, out var resource) && !resource.IsTemplated)
                    {
                        request.MatchedPrimitive = resource;
                    }
                    else
                    {
                        // Fall back to an O(N) lookup, trying to match against each URI template.
                        foreach (var resourceTemplate in resources)
                        {
                            if (resourceTemplate.IsMatch(uri))
                            {
                                request.MatchedPrimitive = resourceTemplate;
                                break;
                            }
                        }
                    }
                }

                try
                {
                    var result = await handler(request, cancellationToken).ConfigureAwait(false);
                    ReadResourceCompleted(request.Params?.Uri ?? string.Empty);
                    return result;
                }
                catch (Exception e)
                {
                    ReadResourceError(request.Params?.Uri ?? string.Empty, e);
                    throw;
                }
            });
        subscribeHandler = BuildFilterPipeline(subscribeHandler, options.Filters.Request.SubscribeToResourcesFilters);
        unsubscribeHandler = BuildFilterPipeline(unsubscribeHandler, options.Filters.Request.UnsubscribeFromResourcesFilters);

        ServerCapabilities.Resources.ListChanged = listChanged;
        ServerCapabilities.Resources.Subscribe = subscribe;

        SetHandler(
            RequestMethods.ResourcesList,
            listResourcesHandler,
            McpJsonUtilities.JsonContext.Default.ListResourcesRequestParams,
            McpJsonUtilities.JsonContext.Default.ListResourcesResult);

        SetHandler(
            RequestMethods.ResourcesTemplatesList,
            listResourceTemplatesHandler,
            McpJsonUtilities.JsonContext.Default.ListResourceTemplatesRequestParams,
            McpJsonUtilities.JsonContext.Default.ListResourceTemplatesResult);

        SetHandler(
            RequestMethods.ResourcesRead,
            readResourceHandler,
            McpJsonUtilities.JsonContext.Default.ReadResourceRequestParams,
            McpJsonUtilities.JsonContext.Default.ReadResourceResult);

        SetHandler(
            RequestMethods.ResourcesSubscribe,
            subscribeHandler,
            McpJsonUtilities.JsonContext.Default.SubscribeRequestParams,
            McpJsonUtilities.JsonContext.Default.EmptyResult);

        SetHandler(
            RequestMethods.ResourcesUnsubscribe,
            unsubscribeHandler,
            McpJsonUtilities.JsonContext.Default.UnsubscribeRequestParams,
            McpJsonUtilities.JsonContext.Default.EmptyResult);
    }

    private void ConfigurePrompts(McpServerOptions options)
    {
        var listPromptsHandler = options.Handlers.ListPromptsHandler;
        var getPromptHandler = options.Handlers.GetPromptHandler;
        var prompts = options.PromptCollection;
        var promptsCapability = options.Capabilities?.Prompts;

        if (listPromptsHandler is null && getPromptHandler is null && prompts is null &&
            promptsCapability is null)
        {
            return;
        }

        ServerCapabilities.Prompts = new();

        listPromptsHandler ??= (static async (_, __) => new ListPromptsResult());
        getPromptHandler ??= (static async (request, _) => throw new McpProtocolException($"Unknown prompt: '{request.Params?.Name}'", McpErrorCode.InvalidParams));
        var listChanged = promptsCapability?.ListChanged;

        // Handle tools provided via DI by augmenting the handlers to incorporate them.
        if (prompts is not null)
        {
            var originalListPromptsHandler = listPromptsHandler;
            listPromptsHandler = async (request, cancellationToken) =>
            {
                ListPromptsResult result = originalListPromptsHandler is not null ?
                    await originalListPromptsHandler(request, cancellationToken).ConfigureAwait(false) :
                    new();

                if (request.Params?.Cursor is null)
                {
                    foreach (var p in prompts)
                    {
                        result.Prompts.Add(p.ProtocolPrompt);
                    }
                }

                return result;
            };

            var originalGetPromptHandler = getPromptHandler;
            getPromptHandler = (request, cancellationToken) =>
            {
                if (request.MatchedPrimitive is McpServerPrompt prompt)
                {
                    return prompt.GetAsync(request, cancellationToken);
                }

                return originalGetPromptHandler(request, cancellationToken);
            };

            listChanged = true;
        }

        listPromptsHandler = BuildFilterPipeline(listPromptsHandler, options.Filters.Request.ListPromptsFilters);
        getPromptHandler = BuildFilterPipeline(getPromptHandler, options.Filters.Request.GetPromptFilters, handler =>
            async (request, cancellationToken) =>
            {
                // Initial handler that sets MatchedPrimitive
                if (request.Params?.Name is { } promptName && prompts is not null &&
                    prompts.TryGetPrimitive(promptName, out var prompt))
                {
                    request.MatchedPrimitive = prompt;
                }

                try
                {
                    var result = await handler(request, cancellationToken).ConfigureAwait(false);
                    GetPromptCompleted(request.Params?.Name ?? string.Empty);
                    return result;
                }
                catch (Exception e)
                {
                    GetPromptError(request.Params?.Name ?? string.Empty, e);
                    throw;
                }
            });

        ServerCapabilities.Prompts.ListChanged = listChanged;

        SetHandler(
            RequestMethods.PromptsList,
            listPromptsHandler,
            McpJsonUtilities.JsonContext.Default.ListPromptsRequestParams,
            McpJsonUtilities.JsonContext.Default.ListPromptsResult);

        SetHandler(
            RequestMethods.PromptsGet,
            getPromptHandler,
            McpJsonUtilities.JsonContext.Default.GetPromptRequestParams,
            McpJsonUtilities.JsonContext.Default.GetPromptResult);
    }

    private void ConfigureTools(McpServerOptions options)
    {
        var listToolsHandler = options.Handlers.ListToolsHandler;
        var callToolHandler = options.Handlers.CallToolHandler;
        var tools = options.ToolCollection;
        var toolsCapability = options.Capabilities?.Tools;

        if (listToolsHandler is null && callToolHandler is null && tools is null &&
            toolsCapability is null)
        {
            return;
        }

        ServerCapabilities.Tools = new();

        listToolsHandler ??= (static async (_, __) => new ListToolsResult());
        callToolHandler ??= (static async (request, _) => throw new McpProtocolException($"Unknown tool: '{request.Params?.Name}'", McpErrorCode.InvalidParams));
        var listChanged = toolsCapability?.ListChanged;

        // Handle tools provided via DI by augmenting the handlers to incorporate them.
        if (tools is not null)
        {
            var originalListToolsHandler = listToolsHandler;
            listToolsHandler = async (request, cancellationToken) =>
            {
                ListToolsResult result = originalListToolsHandler is not null ?
                    await originalListToolsHandler(request, cancellationToken).ConfigureAwait(false) :
                    new();

                if (request.Params?.Cursor is null)
                {
                    foreach (var t in tools)
                    {
                        result.Tools.Add(t.ProtocolTool);
                    }
                }

                return result;
            };

            var originalCallToolHandler = callToolHandler;
            var taskStore = options.TaskStore;
            var sendNotifications = options.SendTaskStatusNotifications;
            callToolHandler = async (request, cancellationToken) =>
            {
                if (request.MatchedPrimitive is McpServerTool tool)
                {
                    var taskSupport = tool.ProtocolTool.Execution?.TaskSupport ?? ToolTaskSupport.Forbidden;

                    // Check if this is a task-augmented request
                    if (request.Params?.Task is { } taskMetadata)
                    {
                        // Validate tool-level task support
                        if (taskSupport is ToolTaskSupport.Forbidden)
                        {
                            throw new McpProtocolException(
                                $"Tool '{tool.ProtocolTool.Name}' does not support task-augmented execution.",
                                McpErrorCode.InvalidParams);
                        }

                        // When DeferTaskCreation is enabled, run the handler through the normal
                        // MRTR-wrapped path with deferred task context, allowing ephemeral MRTR
                        // exchanges before the tool calls CreateTaskAsync().
                        if (tool.DeferTaskCreation)
                        {
                            // Attach deferred task info to the MrtrContext so CreateTaskAsync()
                            // and AwaitMrtrHandlerAsync can use it. The MrtrContext was already
                            // created by WrapHandlerWithMrtr and set on the per-request server.
                            if (request.Server is DestinationBoundMcpServer destinationServer &&
                                destinationServer.ActiveMrtrContext is { } mrtrContext)
                            {
                                mrtrContext.DeferredTask = new DeferredTaskInfo
                                {
                                    TaskMetadata = taskMetadata,
                                    OriginalRequestId = request.JsonRpcRequest.Id,
                                    OriginalRequest = request.JsonRpcRequest,
                                    TaskStore = taskStore!,
                                    SendNotifications = sendNotifications,
                                };
                            }

                            // Execute normally — the MRTR wrapper (WrapHandlerWithMrtr) will handle
                            // racing between handler completion, MRTR exchanges, and task creation.
                            return await tool.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
                        }

                        // Task augmentation requested with immediate creation
                        return await ExecuteToolAsTaskAsync(tool, request, taskMetadata, taskStore, sendNotifications, cancellationToken).ConfigureAwait(false);
                    }

                    // Validate that required task support is satisfied
                    if (taskSupport is ToolTaskSupport.Required)
                    {
                        throw new McpProtocolException(
                            $"Tool '{tool.ProtocolTool.Name}' requires task-augmented execution. " +
                            "Include a 'task' parameter with the request.",
                            McpErrorCode.InvalidParams);
                    }

                    // Normal synchronous execution
                    return await tool.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
                }

                return await originalCallToolHandler(request, cancellationToken).ConfigureAwait(false);
            };

            listChanged = true;
        }

        listToolsHandler = BuildFilterPipeline(listToolsHandler, options.Filters.Request.ListToolsFilters);
        callToolHandler = BuildFilterPipeline(callToolHandler, options.Filters.Request.CallToolFilters, handler =>
            async (request, cancellationToken) =>
            {
                // Initial handler that sets MatchedPrimitive
                if (request.Params?.Name is { } toolName && tools is not null &&
                    tools.TryGetPrimitive(toolName, out var tool))
                {
                    request.MatchedPrimitive = tool;
                }

                try
                {
                    var result = await handler(request, cancellationToken).ConfigureAwait(false);

                    // Don't log here for task-augmented calls; logging happens asynchronously
                    // in ExecuteToolAsTaskAsync when the tool actually completes.
                    if (result.Task is null)
                    {
                        ToolCallCompleted(request.Params?.Name ?? string.Empty, result.IsError is true);
                    }

                    return result;
                }
                catch (Exception e)
                {
                    // Skip logging for OperationCanceledException during server disposal —
                    // MRTR handler cancellation during session teardown is expected, not an error.
                    // Skip logging for IncompleteResultException — it's normal MRTR control flow,
                    // not an error (the low-level API uses it to signal an IncompleteResult).
                    if (!(e is OperationCanceledException && _disposed) && e is not IncompleteResultException)
                    {
                        ToolCallError(request.Params?.Name ?? string.Empty, e);
                    }

                    if ((e is OperationCanceledException && cancellationToken.IsCancellationRequested) || e is McpProtocolException || e is IncompleteResultException)
                    {
                        throw;
                    }

                    return new()
                    {
                        IsError = true,
                        Content = [new TextContentBlock
                        {
                            Text = e is McpException ?
                                $"An error occurred invoking '{request.Params?.Name}': {e.Message}" :
                                $"An error occurred invoking '{request.Params?.Name}'.",
                        }],
                    };
                }
            });

        ServerCapabilities.Tools.ListChanged = listChanged;

        SetHandler(
            RequestMethods.ToolsList,
            listToolsHandler,
            McpJsonUtilities.JsonContext.Default.ListToolsRequestParams,
            McpJsonUtilities.JsonContext.Default.ListToolsResult);

        SetHandler(
            RequestMethods.ToolsCall,
            callToolHandler,
            McpJsonUtilities.JsonContext.Default.CallToolRequestParams,
            McpJsonUtilities.JsonContext.Default.CallToolResult);
    }

    private void ConfigureTasks(McpServerOptions options)
    {
        var taskStore = options.TaskStore;

        // If no task store is configured, tasks are not supported
        if (taskStore is null)
        {
            return;
        }

        // Advertise task support in server capabilities
        ServerCapabilities.Tasks = new McpTasksCapability
        {
            List = new ListMcpTasksCapability(),
            Cancel = new CancelMcpTasksCapability(),
            Requests = new RequestMcpTasksCapability
            {
                Tools = new ToolsMcpTasksCapability
                {
                    Call = new CallToolMcpTasksCapability()
                }
            }
        };

        // tasks/get handler - Retrieve task status
        McpRequestHandler<GetTaskRequestParams, McpTask> getTaskHandler = async (request, cancellationToken) =>
        {
            if (request.Params?.TaskId is not { } taskId)
            {
                throw new McpProtocolException("Missing required parameter 'taskId'", McpErrorCode.InvalidParams);
            }

            var task = await taskStore.GetTaskAsync(taskId, SessionId, cancellationToken).ConfigureAwait(false);
            if (task is null)
            {
                throw new McpProtocolException($"Task not found: '{taskId}'", McpErrorCode.InvalidParams);
            }

            return task;
        };

        // tasks/result handler - Retrieve task result (blocking until terminal status)
        McpRequestHandler<GetTaskPayloadRequestParams, JsonElement> getTaskResultHandler = (request, cancellationToken) =>
        {
            return new ValueTask<JsonElement>(GetTaskResultAsync(request, cancellationToken));

            async Task<JsonElement> GetTaskResultAsync(RequestContext<GetTaskPayloadRequestParams> request, CancellationToken cancellationToken)
            {
                if (request.Params?.TaskId is not { } taskId)
                {
                    throw new McpProtocolException("Missing required parameter 'taskId'", McpErrorCode.InvalidParams);
                }

                // Poll until task reaches terminal status
                while (true)
                {
                    McpTask? task = await taskStore.GetTaskAsync(taskId, SessionId, cancellationToken).ConfigureAwait(false);
                    if (task is null)
                    {
                        throw new McpProtocolException($"Task not found: '{taskId}'", McpErrorCode.InvalidParams);
                    }

                    // If terminal, break and retrieve result
                    if (task.Status is McpTaskStatus.Completed or McpTaskStatus.Failed or McpTaskStatus.Cancelled)
                    {
                        break;
                    }

                    // Poll according to task's pollInterval (default 1 second)
                    var pollInterval = task.PollInterval ?? TimeSpan.FromSeconds(1);
                    await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
                }

                // Retrieve the stored result - already stored as JsonElement
                return await taskStore.GetTaskResultAsync(taskId, SessionId, cancellationToken).ConfigureAwait(false);
            }
        };

        // tasks/list handler - List tasks with pagination
        McpRequestHandler<ListTasksRequestParams, ListTasksResult> listTasksHandler = async (request, cancellationToken) =>
        {
            var cursor = request.Params?.Cursor;
            return await taskStore.ListTasksAsync(cursor, SessionId, cancellationToken).ConfigureAwait(false);
        };

        // tasks/cancel handler - Cancel a task
        McpRequestHandler<CancelMcpTaskRequestParams, McpTask> cancelTaskHandler = async (request, cancellationToken) =>
        {
            if (request.Params?.TaskId is not { } taskId)
            {
                throw new McpProtocolException("Missing required parameter 'taskId'", McpErrorCode.InvalidParams);
            }

            // Signal cancellation if task is still running
            _taskCancellationTokenProvider!.Cancel(taskId);

            // Delegate to task store - it handles idempotent cancellation
            var task = await taskStore.CancelTaskAsync(taskId, SessionId, cancellationToken).ConfigureAwait(false);
            if (task is null)
            {
                throw new McpProtocolException($"Task not found: '{taskId}'", McpErrorCode.InvalidParams);
            }

            return task;
        };

        // Register handlers
        SetHandler(
            RequestMethods.TasksGet,
            getTaskHandler,
            McpJsonUtilities.JsonContext.Default.GetTaskRequestParams,
            McpJsonUtilities.JsonContext.Default.McpTask);

        SetHandler(
            RequestMethods.TasksResult,
            getTaskResultHandler,
            McpJsonUtilities.JsonContext.Default.GetTaskPayloadRequestParams,
            McpJsonUtilities.JsonContext.Default.JsonElement);

        SetHandler(
            RequestMethods.TasksList,
            listTasksHandler,
            McpJsonUtilities.JsonContext.Default.ListTasksRequestParams,
            McpJsonUtilities.JsonContext.Default.ListTasksResult);

        SetHandler(
            RequestMethods.TasksCancel,
            cancelTaskHandler,
            McpJsonUtilities.JsonContext.Default.CancelMcpTaskRequestParams,
            McpJsonUtilities.JsonContext.Default.McpTask);
    }

    private void ConfigureLogging(McpServerOptions options)
    {
        // We don't require that the handler be provided, as we always store the provided log level to the server.
        var setLoggingLevelHandler = options.Handlers.SetLoggingLevelHandler;

        // Apply filters to the handler
        if (setLoggingLevelHandler is not null)
        {
            setLoggingLevelHandler = BuildFilterPipeline(setLoggingLevelHandler, options.Filters.Request.SetLoggingLevelFilters);
        }

        ServerCapabilities.Logging = new();

        _requestHandlers.Set(
            RequestMethods.LoggingSetLevel,
            (request, jsonRpcRequest, cancellationToken) =>
            {
                // Store the provided level.
                if (request is not null)
                {
                    if (_loggingLevel is null)
                    {
                        Interlocked.CompareExchange(ref _loggingLevel, new(request.Level), null);
                    }

                    _loggingLevel.Value = request.Level;
                }

                // If a handler was provided, now delegate to it.
                if (setLoggingLevelHandler is not null)
                {
                    return InvokeHandlerAsync(setLoggingLevelHandler, request, jsonRpcRequest, cancellationToken);
                }

                // Otherwise, consider it handled.
                return new ValueTask<EmptyResult>(EmptyResult.Instance);
            },
            McpJsonUtilities.JsonContext.Default.SetLevelRequestParams,
            McpJsonUtilities.JsonContext.Default.EmptyResult);
    }

    private ValueTask<TResult> InvokeHandlerAsync<TParams, TResult>(
        McpRequestHandler<TParams, TResult> handler,
        TParams? args,
        JsonRpcRequest jsonRpcRequest,
        CancellationToken cancellationToken = default)
    {
        return _servicesScopePerRequest ?
            InvokeScopedAsync(handler, args, jsonRpcRequest, cancellationToken) :
            handler(new(CreateDestinationBoundServer(jsonRpcRequest), jsonRpcRequest) { Params = args }, cancellationToken);

        async ValueTask<TResult> InvokeScopedAsync(
            McpRequestHandler<TParams, TResult> handler,
            TParams? args,
            JsonRpcRequest jsonRpcRequest,
            CancellationToken cancellationToken)
        {
            var scope = Services?.GetService<IServiceScopeFactory>()?.CreateAsyncScope();
            try
            {
                return await handler(
                    new RequestContext<TParams>(CreateDestinationBoundServer(jsonRpcRequest), jsonRpcRequest)
                    {
                        Services = scope?.ServiceProvider ?? Services,
                        Params = args
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (scope is not null)
                {
                    await scope.Value.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Creates a per-request <see cref="DestinationBoundMcpServer"/> and attaches any
    /// MRTR context that was set on the request by <see cref="WrapHandlerWithMrtr"/>.
    /// </summary>
    private DestinationBoundMcpServer CreateDestinationBoundServer(JsonRpcRequest jsonRpcRequest)
    {
        var server = new DestinationBoundMcpServer(this, jsonRpcRequest.Context?.RelatedTransport);

        if (jsonRpcRequest.Context?.MrtrContext is { } mrtrContext)
        {
            server.ActiveMrtrContext = mrtrContext;
        }

        return server;
    }

    private void SetHandler<TParams, TResult>(
        string method,
        McpRequestHandler<TParams, TResult> handler,
        JsonTypeInfo<TParams> requestTypeInfo,
        JsonTypeInfo<TResult> responseTypeInfo)
    {
        _requestHandlers.Set(method,
            (request, jsonRpcRequest, cancellationToken) =>
                InvokeHandlerAsync(handler, request, jsonRpcRequest, cancellationToken),
            requestTypeInfo, responseTypeInfo);
    }

    private static McpRequestHandler<TParams, TResult> BuildFilterPipeline<TParams, TResult>(
        McpRequestHandler<TParams, TResult> baseHandler,
        IList<McpRequestFilter<TParams, TResult>> filters,
        McpRequestFilter<TParams, TResult>? initialHandler = null)
    {
        var current = baseHandler;

        for (int i = filters.Count - 1; i >= 0; i--)
        {
            current = filters[i](current);
        }

        if (initialHandler is not null)
        {
            current = initialHandler(current);
        }

        return current;
    }

    private JsonRpcMessageFilter BuildMessageFilterPipeline(IList<McpMessageFilter> filters)
    {
        if (filters.Count == 0)
        {
            return next => next;
        }

        return next =>
        {
            // Build the handler chain from the filters.
            // The innermost handler calls the provided 'next' delegate with the message from the context.
            McpMessageHandler baseHandler = async (context, cancellationToken) =>
            {
                await next(context.JsonRpcMessage, cancellationToken).ConfigureAwait(false);
            };

            var current = baseHandler;
            for (int i = filters.Count - 1; i >= 0; i--)
            {
                current = filters[i](current);
            }

            // Return the handler that creates a MessageContext and invokes the pipeline.
            return async (message, cancellationToken) =>
            {
                // Ensure message has a Context so Items can be shared through the pipeline
                message.Context ??= new();
                var context = new MessageContext(new DestinationBoundMcpServer(this, message.Context.RelatedTransport), message);
                await current(context, cancellationToken).ConfigureAwait(false);
            };
        };
    }

    private void UpdateEndpointNameWithClientInfo()
    {
        if (ClientInfo is null)
        {
            return;
        }

        _endpointName = $"{_serverOnlyEndpointName}, Client ({ClientInfo.Name} {ClientInfo.Version})";
    }

    /// <summary>Maps a <see cref="LogLevel"/> to a <see cref="LoggingLevel"/>.</summary>
    internal static LoggingLevel ToLoggingLevel(LogLevel level) =>
        level switch
        {
            LogLevel.Trace => Protocol.LoggingLevel.Debug,
            LogLevel.Debug => Protocol.LoggingLevel.Debug,
            LogLevel.Information => Protocol.LoggingLevel.Info,
            LogLevel.Warning => Protocol.LoggingLevel.Warning,
            LogLevel.Error => Protocol.LoggingLevel.Error,
            LogLevel.Critical => Protocol.LoggingLevel.Critical,
            _ => Protocol.LoggingLevel.Emergency,
        };

    /// <summary>
    /// Checks whether the negotiated protocol version enables MRTR.
    /// </summary>
    internal bool ClientSupportsMrtr() =>
        _negotiatedProtocolVersion is not null &&
        _negotiatedProtocolVersion == ServerOptions.ExperimentalProtocolVersion;

    /// <summary>
    /// Wraps MRTR-eligible request handlers so that when a handler calls ElicitAsync/SampleAsync,
    /// an IncompleteResult is returned early and the handler is suspended until the retry arrives.
    /// </summary>
    private void ConfigureMrtr()
    {
        // Wrap all methods that may trigger MRTR (server calling ElicitAsync/SampleAsync/RequestRootsAsync
        // during handler execution). These methods may produce IncompleteResult if the handler needs input.
        WrapHandlerWithMrtr(RequestMethods.ToolsCall);
        WrapHandlerWithMrtr(RequestMethods.PromptsGet);
        WrapHandlerWithMrtr(RequestMethods.ResourcesRead);
    }

    /// <summary>
    /// Replaces an existing request handler entry with an MRTR-aware wrapper that supports
    /// handler suspension and IncompleteResult responses.
    /// </summary>
    private void WrapHandlerWithMrtr(string method)
    {
        if (!_requestHandlers.TryGetValue(method, out var originalHandler))
        {
            return;
        }

        _requestHandlers[method] = async (request, cancellationToken) =>
        {
            // In stateless mode, each request creates a new server instance that never saw the
            // initialize handshake, so _negotiatedProtocolVersion is null. Pick it up from the
            // Mcp-Protocol-Version header that the transport layer flowed via JsonRpcMessageContext.
            if (_negotiatedProtocolVersion is null &&
                request.Context?.ProtocolVersion is { } headerProtocolVersion)
            {
                _negotiatedProtocolVersion = headerProtocolVersion;
            }

            // Check for MRTR retry: if requestState is present, look up the continuation.
            if (request.Params is JsonObject paramsObj &&
                paramsObj.TryGetPropertyValue("requestState", out var requestStateNode) &&
                requestStateNode?.GetValueKind() == JsonValueKind.String &&
                requestStateNode.GetValue<string>() is { } requestState)
            {
                if (_mrtrContinuations.TryRemove(requestState, out var existingContinuation))
                {
                    // High-level MRTR retry: resume the suspended handler with client responses.
                    IDictionary<string, InputResponse>? inputResponses = null;
                    if (paramsObj.TryGetPropertyValue("inputResponses", out var responsesNode) && responsesNode is not null)
                    {
                        inputResponses = JsonSerializer.Deserialize(responsesNode, McpJsonUtilities.JsonContext.Default.IDictionaryStringInputResponse);
                    }

                    var nextExchangeTask = existingContinuation.MrtrContext.ResetForNextExchange(existingContinuation.PendingExchange!);

                    var exchange = existingContinuation.PendingExchange!;
                    if (inputResponses is not null &&
                        inputResponses.TryGetValue(exchange.Key, out var response))
                    {
                        if (!exchange.ResponseTcs.TrySetResult(response))
                        {
                            throw new McpProtocolException(
                                $"MRTR exchange '{exchange.Key}' was already completed (possibly cancelled).",
                                McpErrorCode.InternalError);
                        }
                    }
                    else
                    {
                        if (!exchange.ResponseTcs.TrySetException(
                            new McpProtocolException($"Missing input response for key '{exchange.Key}'.", McpErrorCode.InvalidParams)))
                        {
                            throw new McpProtocolException(
                                $"MRTR exchange '{exchange.Key}' was already completed (possibly cancelled).",
                                McpErrorCode.InternalError);
                        }
                    }

                    return await AwaitMrtrHandlerAsync(
                        existingContinuation.HandlerTask, existingContinuation, nextExchangeTask, cancellationToken).ConfigureAwait(false);
                }

                // Low-level MRTR retry or invalid requestState: no continuation found.
                // Fall through to the standard MRTR-aware invocation path below. The retry data
                // (inputResponses, requestState) is already in the deserialized request params
                // for low-level handlers to access, and the MrtrContext will be set up for
                // high-level handlers that call ElicitAsync/SampleAsync.
            }

            // Not a retry, or a retry without a continuation - check if the client supports MRTR
            // and the server is stateful (the high-level await path requires storing continuations).
            if (!ClientSupportsMrtr() || _sessionTransport is StreamableHttpServerTransport { Stateless: true })
            {
                return await InvokeWithIncompleteResultHandlingAsync(originalHandler, request, cancellationToken).ConfigureAwait(false);
            }

            // Start a new MRTR-aware handler invocation.
            var mrtrContext = new MrtrContext();

            // Create a long-lived CTS for the handler that survives across retries.
            // The original request's combinedCts will be disposed when this lambda returns,
            // breaking the cancellation chain. This CTS keeps the handler cancellable.
            // Like Kestrel's HttpContext.RequestAborted, the CTS is never disposed — Cancel()
            // is thread-safe with itself, and not disposing avoids deadlock risks from
            // calling Cancel/Dispose inside locks or Interlocked guards.
            var handlerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Flow the MrtrContext to the handler via the request's Context, where
            // CreateDestinationBoundServer will pick it up to set on the per-request server.
            (request.Context ??= new()).MrtrContext = mrtrContext;
            var handlerTask = originalHandler(request, handlerCts.Token);

            // Wrap handler state into a continuation for lifecycle management across retries.
            var continuation = new MrtrContinuation(handlerCts, handlerTask, mrtrContext);

            // Track the handler task for lifecycle management. The observer logs unhandled
            // exceptions and decrements _mrtrInFlightCount when the handler completes,
            // mirroring how McpSessionHandler tracks in-flight handlers.
            Interlocked.Increment(ref _mrtrInFlightCount);
            _ = ObserveHandlerCompletionAsync(handlerTask);

            return await AwaitMrtrHandlerAsync(
                handlerTask, continuation, mrtrContext.InitialExchangeTask, cancellationToken).ConfigureAwait(false);
        };
    }

    /// <summary>
    /// Invokes a handler and catches <see cref="IncompleteResultException"/> to convert it to an
    /// <see cref="IncompleteResult"/> JSON response. In stateless mode, the exception is always
    /// serialized because the server cannot determine client MRTR support. In stateful mode,
    /// if MRTR is not supported, the exception is wrapped with a descriptive message.
    /// </summary>
    private async Task<JsonNode?> InvokeWithIncompleteResultHandlingAsync(
        Func<JsonRpcRequest, CancellationToken, Task<JsonNode?>> handler,
        JsonRpcRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }
        catch (IncompleteResultException ex)
        {
            // Allow the IncompleteResult if the client supports MRTR or the server is stateless
            // (in stateless mode, the tool handler has explicitly chosen to return an IncompleteResult
            // via the low-level API, so we trust that decision regardless of negotiated version).
            if (!ClientSupportsMrtr() && _sessionTransport is not StreamableHttpServerTransport { Stateless: true })
            {
                throw new McpException(
                    "A tool handler returned an incomplete result, but the client does not support Multi Round-Trip Requests (MRTR). " +
                    "Ensure both the server and client have ExperimentalProtocolVersion configured to enable MRTR.",
                    ex);
            }

            return SerializeIncompleteResult(ex.IncompleteResult);
        }
    }

    /// <summary>
    /// Awaits the outcome of an MRTR-enabled handler invocation.
    /// If the handler completes, returns its result. If an exchange arrives (handler needs input),
    /// builds and returns an IncompleteResult and stores the continuation for future retries.
    /// If the handler throws <see cref="IncompleteResultException"/>, the result is returned directly
    /// without storing a continuation (low-level MRTR path).
    /// When deferred task creation is enabled, also races against the task creation signal.
    /// </summary>
    private async Task<JsonNode?> AwaitMrtrHandlerAsync(
        Task<JsonNode?> handlerTask,
        MrtrContinuation continuation,
        Task<MrtrExchange> exchangeTask,
        CancellationToken cancellationToken)
    {
        // Link the current request's cancellation to the handler's long-lived CTS.
        // On the initial call this is redundant (handlerCts is already linked to cancellationToken)
        // but on retries this is critical: the retry's combinedCts cancellation must flow to the handler.
        // This is how notifications/cancelled for the retry's request ID reaches the handler.
        var registration = cancellationToken.Register(
            static state => ((MrtrContinuation)state!).CancelHandler(), continuation);

        try
        {
            var deferredTask = continuation.MrtrContext.DeferredTask;

            // Race handler against MRTR exchange and optionally the deferred task creation signal.
            Task completedTask;
            if (deferredTask is not null)
            {
                completedTask = await Task.WhenAny(handlerTask, exchangeTask, deferredTask.SignalTask).ConfigureAwait(false);
            }
            else
            {
                completedTask = await Task.WhenAny(handlerTask, exchangeTask).ConfigureAwait(false);
            }

            if (completedTask == handlerTask)
            {
                // Handler completed - return its result, propagate its exception, or handle IncompleteResultException.
                return await AwaitHandlerWithIncompleteResultHandlingAsync(handlerTask).ConfigureAwait(false);
            }

            if (deferredTask is not null && completedTask == deferredTask.SignalTask)
            {
                // Handler called CreateTaskAsync() — transition to task mode.
                return await HandleDeferredTaskCreationAsync(handlerTask, continuation, deferredTask, cancellationToken).ConfigureAwait(false);
            }

            // Exchange arrived - handler needs input from the client (high-level MRTR path).
            var exchange = await exchangeTask.ConfigureAwait(false);

            var correlationId = Guid.NewGuid().ToString("N");
            var incompleteResult = new IncompleteResult
            {
                InputRequests = new Dictionary<string, InputRequest> { [exchange.Key] = exchange.InputRequest },
                RequestState = correlationId,
            };

            // Store the continuation so the retry can resume the handler.
            continuation.PendingExchange = exchange;
            _mrtrContinuations[correlationId] = continuation;

            return SerializeIncompleteResult(incompleteResult);
        }
        finally
        {
            registration.Dispose();
        }
    }

    /// <summary>
    /// Fire-and-forget observer for an MRTR handler task. Logs unhandled exceptions at Error
    /// level and decrements <see cref="_mrtrInFlightCount"/> when the handler completes, following
    /// the same in-flight tracking pattern as <see cref="McpSessionHandler"/>.
    /// </summary>
    private async Task ObserveHandlerCompletionAsync(Task<JsonNode?> handlerTask)
    {
        try
        {
            await handlerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Handler cancelled — expected lifecycle event (disposal, client cancel, session shutdown).
        }
        catch (IncompleteResultException)
        {
            // Low-level MRTR: handler explicitly signaling an IncompleteResult. Not an error.
        }
        catch (Exception ex)
        {
            MrtrHandlerError(ex);
        }
        finally
        {
            if (Interlocked.Decrement(ref _mrtrInFlightCount) == 0)
            {
                _allMrtrHandlersCompleted.TrySetResult(true);
            }
        }
    }

    /// <summary>
    /// Awaits a handler task, catching <see cref="IncompleteResultException"/> to convert it to an
    /// <see cref="IncompleteResult"/> JSON response without storing a continuation.
    /// </summary>
    private static async Task<JsonNode?> AwaitHandlerWithIncompleteResultHandlingAsync(Task<JsonNode?> handlerTask)
    {
        try
        {
            return await handlerTask.ConfigureAwait(false);
        }
        catch (IncompleteResultException ex)
        {
            return SerializeIncompleteResult(ex.IncompleteResult);
        }
    }

    private static JsonNode? SerializeIncompleteResult(IncompleteResult incompleteResult) =>
        JsonSerializer.SerializeToNode(incompleteResult, McpJsonUtilities.JsonContext.Default.IncompleteResult);

    /// <summary>
    /// Handles the transition from ephemeral MRTR to task-based execution when the handler
    /// calls <see cref="McpServer.CreateTaskAsync(CancellationToken)"/>.
    /// Creates the task, acknowledges the handler, re-links the handler CTS to the task's
    /// cancellation token, and returns CreateTaskResult to the client.
    /// </summary>
    private async Task<JsonNode?> HandleDeferredTaskCreationAsync(
        Task<JsonNode?> handlerTask,
        MrtrContinuation continuation,
        DeferredTaskInfo deferredTask,
        CancellationToken cancellationToken)
    {
        var taskStore = deferredTask.TaskStore;
        var sendNotifications = deferredTask.SendNotifications;

        Protocol.McpTask mcpTask;
        CancellationToken taskCancellationToken;
        try
        {
            // Create the task in the task store.
            mcpTask = await taskStore.CreateTaskAsync(
                deferredTask.TaskMetadata,
                deferredTask.OriginalRequestId,
                deferredTask.OriginalRequest,
                SessionId,
                cancellationToken).ConfigureAwait(false);

            // Register the task for TTL-based cancellation.
            taskCancellationToken = _taskCancellationTokenProvider!.RequestToken(mcpTask.TaskId, mcpTask.TimeToLive);

            // Re-link the handler's CTS to the task's cancellation token so handler
            // cancellation tracks the task lifecycle (TTL expiration, explicit cancel)
            // instead of the original request.
            taskCancellationToken.Register(
                static state => ((MrtrContinuation)state!).CancelHandler(), continuation);

            // Update task status to working.
            var workingTask = await taskStore.UpdateTaskStatusAsync(
                mcpTask.TaskId,
                McpTaskStatus.Working,
                null,
                SessionId,
                CancellationToken.None).ConfigureAwait(false);

            if (sendNotifications)
            {
                _ = NotifyTaskStatusAsync(workingTask, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            // If task creation fails, propagate the exception to the handler
            // so CreateTaskAsync() throws instead of blocking forever.
            deferredTask.AcknowledgeFailure(ex);
            throw;
        }

        // Acknowledge the handler so CreateTaskAsync() returns and the handler continues.
        deferredTask.AcknowledgeTaskCreation(new DeferredTaskCreationResult
        {
            TaskId = mcpTask.TaskId,
            SessionId = SessionId,
            TaskStore = taskStore,
            SendNotifications = sendNotifications,
            NotifyTaskStatusFunc = NotifyTaskStatusAsync,
            TaskCancellationToken = taskCancellationToken,
        });

        // Track the handler task in the background. The handler is already tracked by
        // ObserveHandlerCompletionAsync (via _mrtrInFlightCount), so no additional
        // in-flight tracking is needed here — just status updates.
        _ = TrackDeferredHandlerTaskAsync(handlerTask, mcpTask, taskStore, sendNotifications);

        // Return CreateTaskResult to the client.
        var createTaskResult = new CallToolResult { Task = mcpTask };
        return JsonSerializer.SerializeToNode(createTaskResult, McpJsonUtilities.JsonContext.Default.CallToolResult);
    }

    /// <summary>
    /// Tracks a deferred handler task after task creation, updating task status and storing results.
    /// The handler task is already tracked by <see cref="ObserveHandlerCompletionAsync"/> for
    /// in-flight counting and error logging.
    /// </summary>
    private async Task TrackDeferredHandlerTaskAsync(
        Task<JsonNode?> handlerTask,
        Protocol.McpTask mcpTask,
        IMcpTaskStore taskStore,
        bool sendNotifications)
    {
        try
        {
            var resultNode = await handlerTask.ConfigureAwait(false);

            CallToolResult? result = null;
            if (resultNode is not null)
            {
                result = JsonSerializer.Deserialize(resultNode, McpJsonUtilities.JsonContext.Default.CallToolResult);
            }

            var finalStatus = result?.IsError is true ? McpTaskStatus.Failed : McpTaskStatus.Completed;
            var resultElement = result is not null
                ? JsonSerializer.SerializeToElement(result, McpJsonUtilities.JsonContext.Default.CallToolResult)
                : default;

            var finalTask = await taskStore.StoreTaskResultAsync(
                mcpTask.TaskId,
                finalStatus,
                resultElement,
                SessionId,
                CancellationToken.None).ConfigureAwait(false);

            if (sendNotifications)
            {
                _ = NotifyTaskStatusAsync(finalTask, CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            // After task creation, any handler cancellation is legitimate —
            // task TTL expiration, explicit tasks/cancel, or session disposal.
        }
        catch (Exception ex)
        {
            // Error logging is already handled by ObserveHandlerCompletionAsync.
            var errorResult = new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = $"Task execution failed: {ex.Message}" }],
            };

            try
            {
                var errorResultElement = JsonSerializer.SerializeToElement(errorResult, McpJsonUtilities.JsonContext.Default.CallToolResult);
                var failedTask = await taskStore.StoreTaskResultAsync(
                    mcpTask.TaskId,
                    McpTaskStatus.Failed,
                    errorResultElement,
                    SessionId,
                    CancellationToken.None).ConfigureAwait(false);

                if (sendNotifications)
                {
                    _ = NotifyTaskStatusAsync(failedTask, CancellationToken.None);
                }
            }
            catch
            {
                // If we can't store the error result, the task will remain in "working" status.
            }
        }
        finally
        {
            _taskCancellationTokenProvider!.Complete(mcpTask.TaskId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "\"{ToolName}\" threw an unhandled exception.")]
    private partial void ToolCallError(string toolName, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "\"{ToolName}\" completed. IsError = {IsError}.")]
    private partial void ToolCallCompleted(string toolName, bool isError);

    [LoggerMessage(Level = LogLevel.Error, Message = "GetPrompt \"{PromptName}\" threw an unhandled exception.")]
    private partial void GetPromptError(string promptName, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "GetPrompt \"{PromptName}\" completed.")]
    private partial void GetPromptCompleted(string promptName);

    [LoggerMessage(Level = LogLevel.Error, Message = "ReadResource \"{ResourceUri}\" threw an unhandled exception.")]
    private partial void ReadResourceError(string resourceUri, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "ReadResource \"{ResourceUri}\" completed.")]
    private partial void ReadResourceCompleted(string resourceUri);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cancelled {Count} pending MRTR continuation(s) during session disposal.")]
    private partial void MrtrContinuationsCancelled(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "An MRTR handler threw an unhandled exception.")]
    private partial void MrtrHandlerError(Exception exception);

    /// <summary>
    /// Executes a tool call as a task and returns a CallToolTaskResult immediately.
    /// </summary>
    private async ValueTask<CallToolResult> ExecuteToolAsTaskAsync(
        McpServerTool tool,
        RequestContext<CallToolRequestParams> request,
        McpTaskMetadata taskMetadata,
        IMcpTaskStore? taskStore,
        bool sendNotifications,
        CancellationToken cancellationToken)
    {
        if (taskStore is null)
        {
            throw new McpProtocolException(
                "Task-augmented requests are not supported. No task store configured.",
                McpErrorCode.InvalidRequest);
        }

        // Create the task in the task store
        var mcpTask = await taskStore.CreateTaskAsync(
            taskMetadata,
            request.JsonRpcRequest.Id,
            request.JsonRpcRequest,
            SessionId,
            cancellationToken).ConfigureAwait(false);

        // Register the task for TTL-based cancellation
        var taskCancellationToken = _taskCancellationTokenProvider!.RequestToken(mcpTask.TaskId, mcpTask.TimeToLive);

        // Execute the tool asynchronously in the background
        _ = Task.Run(async () =>
        {
            // Set up the task execution context for automatic input_required status tracking
            TaskExecutionContext.Current = new TaskExecutionContext
            {
                TaskId = mcpTask.TaskId,
                SessionId = SessionId,
                TaskStore = taskStore,
                SendNotifications = sendNotifications,
                NotifyTaskStatusFunc = NotifyTaskStatusAsync
            };

            // Task-augmented execution is fire-and-forget; MRTR doesn't apply here because
            // the original request was already answered with CreateTaskResult.
            if (request.Server is DestinationBoundMcpServer destinationServer)
            {
                destinationServer.ActiveMrtrContext = null;
            }

            try
            {
                // Update task status to working
                var workingTask = await taskStore.UpdateTaskStatusAsync(
                    mcpTask.TaskId,
                    McpTaskStatus.Working,
                    null, // statusMessage
                    SessionId,
                    CancellationToken.None).ConfigureAwait(false);

                // Send notification if enabled
                if (sendNotifications)
                {
                    _ = NotifyTaskStatusAsync(workingTask, CancellationToken.None);
                }

                // Invoke the tool with task-specific cancellation token
                var result = await tool.InvokeAsync(request, taskCancellationToken).ConfigureAwait(false);
                ToolCallCompleted(request.Params?.Name ?? string.Empty, result.IsError is true);

                // Determine final status based on whether there was an error
                var finalStatus = result.IsError is true ? McpTaskStatus.Failed : McpTaskStatus.Completed;

                // Store the result (serialize to JsonElement)
                var resultElement = JsonSerializer.SerializeToElement(result, McpJsonUtilities.JsonContext.Default.CallToolResult);
                var finalTask = await taskStore.StoreTaskResultAsync(
                    mcpTask.TaskId,
                    finalStatus,
                    resultElement,
                    SessionId,
                    CancellationToken.None).ConfigureAwait(false);

                // Send final notification if enabled
                if (sendNotifications)
                {
                    _ = NotifyTaskStatusAsync(finalTask, CancellationToken.None);
                }
            }
            catch (OperationCanceledException) when (taskCancellationToken.IsCancellationRequested)
            {
                // Task was cancelled via TTL expiration or explicit cancellation.
                // For TTL expiration, the task is deleted so no status update needed.
                // For explicit cancellation, the cancel handler already updates the status.
            }
            catch (Exception ex)
            {
                // Log the error
                ToolCallError(request.Params?.Name ?? string.Empty, ex);

                // Store error result
                var errorResult = new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = $"Task execution failed: {ex.Message}" }],
                };

                try
                {
                    var errorResultElement = JsonSerializer.SerializeToElement(errorResult, McpJsonUtilities.JsonContext.Default.CallToolResult);
                    var failedTask = await taskStore.StoreTaskResultAsync(
                        mcpTask.TaskId,
                        McpTaskStatus.Failed,
                        errorResultElement,
                        SessionId,
                        CancellationToken.None).ConfigureAwait(false);

                    // Send failure notification if enabled
                    if (sendNotifications)
                    {
                        _ = NotifyTaskStatusAsync(failedTask, CancellationToken.None);
                    }
                }
                catch
                {
                    // If we can't store the error result, there's not much we can do
                    // The task will remain in "working" status, which will eventually be cleaned up
                }
            }
            finally
            {
                // Clean up task execution context
                TaskExecutionContext.Current = null;

                // Clean up task cancellation tracking
                _taskCancellationTokenProvider!.Complete(mcpTask.TaskId);
            }
        }, CancellationToken.None);

        // Return the task result immediately
        return new CallToolResult
        {
            Task = mcpTask
        };
    }
}
