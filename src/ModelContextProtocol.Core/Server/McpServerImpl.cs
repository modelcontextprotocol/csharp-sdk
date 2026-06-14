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
#pragma warning disable MCPEXP001, MCPEXP002
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
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _taskCancellationSources = new();
    private readonly ConcurrentDictionary<string, MrtrContinuation> _mrtrContinuations = new();
    private readonly ConcurrentDictionary<RequestId, MrtrContext> _mrtrContextsByRequestId = new();

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
        ConfigureLogging(options);
        ConfigureCompletion(options);
        ConfigureExperimentalAndExtensions(options);
        ConfigureTasks(options);
        ConfigureMrtr();

        // Register any notification handlers that were provided.
        if (options.Handlers.NotificationHandlers is { } notificationHandlers)
        {
            _notificationHandlers.RegisterRange(notificationHandlers);
        }

        // In stateless mode, the server cannot send unsolicited notifications,
        // so listChanged should not be advertised.
        if (transport is StreamableHttpServerTransport { Stateless: true })
        {
            if (ServerCapabilities.Tools is not null)
                ServerCapabilities.Tools.ListChanged = null;
            if (ServerCapabilities.Prompts is not null)
                ServerCapabilities.Prompts.ListChanged = null;
            if (ServerCapabilities.Resources is not null)
                ServerCapabilities.Resources.ListChanged = null;
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
    [Obsolete(Obsoletions.DeprecatedLogging_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
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

        foreach (var kvp in _taskCancellationSources)
        {
            kvp.Value.Cancel();
            kvp.Value.Dispose();
        }
        _taskCancellationSources.Clear();

        // Dispose the session handler - cancels message processing and waits for all
        // in-flight request handlers (including retries in AwaitMrtrHandlerAsync) to complete.
        // After this returns, no new requests can be processed and no new MRTR continuations
        // can be created, so _mrtrContinuations is effectively frozen.
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
                string? protocolVersion = options.ProtocolVersion;
                protocolVersion ??= request?.ProtocolVersion is string clientProtocolVersion &&
                    McpSessionHandler.SupportedProtocolVersions.Contains(clientProtocolVersion) ?
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

    private void ConfigureTasks(McpServerOptions options)
    {
        var getTaskHandler = options.Handlers.GetTaskHandler;
        var updateTaskHandler = options.Handlers.UpdateTaskHandler;
        var cancelTaskHandler = options.Handlers.CancelTaskHandler;
        var taskStore = options.TaskStore;

        // If a task store is provided, wire up handlers from it for any that aren't explicitly set.
        if (taskStore is not null)
        {
            getTaskHandler ??= async (request, cancellationToken) =>
            {
                var info = await taskStore.GetTaskAsync(request.Params!.TaskId, cancellationToken).ConfigureAwait(false);
                return info is null
                    ? throw new McpProtocolException($"Unknown task: '{request.Params.TaskId}'", McpErrorCode.InvalidParams)
                    : ToGetTaskResult(info);
            };

            updateTaskHandler ??= async (request, cancellationToken) =>
            {
                var inputResponses = request.Params!.InputResponses ?? new Dictionary<string, InputResponse>();
                await taskStore.ResolveInputRequestsAsync(request.Params.TaskId, inputResponses, cancellationToken).ConfigureAwait(false);

                return new UpdateTaskResult();
            };

            cancelTaskHandler ??= async (request, cancellationToken) =>
            {
                // Idempotent ack per SEP-2663: always return CancelTaskResult regardless of whether
                // the task was known/cancellable. The store's SetCancelledAsync no-ops for unknown
                // or already-terminal tasks; we still surface a success response to the client.
                await taskStore.SetCancelledAsync(request.Params!.TaskId, cancellationToken).ConfigureAwait(false);

                // Signal the task's CancellationTokenSource if one exists. Whichever side
                // (this handler or the background runner's finally block) wins TryRemove owns disposal,
                // which prevents the runner from observing ObjectDisposedException through cts.Token.
                if (_taskCancellationSources.TryRemove(request.Params.TaskId, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                }

                return new CancelTaskResult();
            };
        }

        if (getTaskHandler is null && updateTaskHandler is null && cancelTaskHandler is null)
        {
            return;
        }

        getTaskHandler ??= (static async (request, _) => throw new McpProtocolException($"Unknown task: '{request.Params?.TaskId}'", McpErrorCode.InvalidParams));
        updateTaskHandler ??= (static async (request, _) => throw new McpProtocolException($"Unknown task: '{request.Params?.TaskId}'", McpErrorCode.InvalidParams));
        cancelTaskHandler ??= (static async (request, _) => throw new McpProtocolException($"Unknown task: '{request.Params?.TaskId}'", McpErrorCode.InvalidParams));

        // Advertise tasks extension in server capabilities.
        ServerCapabilities.Extensions ??= new Dictionary<string, object>();
        ServerCapabilities.Extensions[McpExtensions.Tasks] = new JsonObject();

        SetHandler(
            RequestMethods.TasksGet,
            getTaskHandler,
            McpJsonUtilities.JsonContext.Default.GetTaskRequestParams,
            McpJsonUtilities.JsonContext.Default.GetTaskResult);

        SetHandler(
            RequestMethods.TasksUpdate,
            updateTaskHandler,
            McpJsonUtilities.JsonContext.Default.UpdateTaskRequestParams,
            McpJsonUtilities.JsonContext.Default.UpdateTaskResult);

        SetHandler(
            RequestMethods.TasksCancel,
            cancelTaskHandler,
            McpJsonUtilities.JsonContext.Default.CancelTaskRequestParams,
            McpJsonUtilities.JsonContext.Default.CancelTaskResult);
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
        readResourceHandler ??= (static async (request, _) =>
        {
            var errorCode = McpHttpHeaders.UseInvalidParamsForMissingResource(request.Server.NegotiatedProtocolVersion)
                ? McpErrorCode.InvalidParams
                : McpErrorCode.ResourceNotFound;
            throw new McpProtocolException($"Unknown resource URI: '{request.Params?.Uri}'", errorCode);
        });
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
        var callToolWithTaskHandler = options.Handlers.CallToolWithTaskHandler;
        var tools = options.ToolCollection;
        var toolsCapability = options.Capabilities?.Tools;

        if (listToolsHandler is null && callToolHandler is null && callToolWithTaskHandler is null && tools is null &&
            toolsCapability is null)
        {
            return;
        }

        ServerCapabilities.Tools = new();

        listToolsHandler ??= (static async (_, __) => new ListToolsResult());
        var listChanged = toolsCapability?.ListChanged;

        var callToolFilters = options.Filters.Request.CallToolFilters;
        var callToolWithTaskFilters = options.Filters.Request.CallToolWithTaskFilters;

        // Validate: cannot mix non-task filters/handler with task filters/handler.
        bool hasNonTaskPath = callToolHandler is not null || callToolFilters.Count > 0;
        bool hasTaskPath = callToolWithTaskHandler is not null || callToolWithTaskFilters.Count > 0;

        if (hasNonTaskPath && hasTaskPath)
        {
            throw new InvalidOperationException(
                $"Cannot mix non-task ({nameof(McpServerHandlers.CallToolHandler)}/{nameof(McpRequestFilters.CallToolFilters)}) " +
                $"with task-based ({nameof(McpServerHandlers.CallToolWithTaskHandler)}/{nameof(McpRequestFilters.CallToolWithTaskFilters)}). Use one style or the other.");
        }

        // Handle tools provided via DI by augmenting the list handler.
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

            listChanged = true;
        }

        listToolsHandler = BuildFilterPipeline(listToolsHandler, options.Filters.Request.ListToolsFilters);

        // Build the unified task-augmented handler from one of the two paths.
        if (hasTaskPath)
        {
            // Case 2: task filter + task handler
            callToolWithTaskHandler ??= (static async (request, _) => throw new McpProtocolException($"Unknown tool: '{request.Params?.Name}'", McpErrorCode.InvalidParams));

            // Augment with DI tools.
            if (tools is not null)
            {
                var originalHandler = callToolWithTaskHandler;
                callToolWithTaskHandler = (request, cancellationToken) =>
                {
                    if (request.MatchedPrimitive is McpServerTool tool)
                    {
                        return InvokeToolAsTask(tool, request, cancellationToken);
                    }

                    return originalHandler(request, cancellationToken);
                };
            }

            callToolWithTaskHandler = BuildFilterPipeline(callToolWithTaskHandler, callToolWithTaskFilters, BuildInitialTaskToolFilter(tools));
        }
        else
        {
            // Case 1: non-task filter + non-task handler → apply filters, then convert to task-based
            callToolHandler ??= (static async (request, _) => throw new McpProtocolException($"Unknown tool: '{request.Params?.Name}'", McpErrorCode.InvalidParams));

            // Augment with DI tools.
            if (tools is not null)
            {
                var originalHandler = callToolHandler;
                callToolHandler = (request, cancellationToken) =>
                {
                    if (request.MatchedPrimitive is McpServerTool tool)
                    {
                        return tool.InvokeAsync(request, cancellationToken);
                    }

                    return originalHandler(request, cancellationToken);
                };
            }

            callToolHandler = BuildFilterPipeline(callToolHandler, callToolFilters, BuildInitialCallToolFilter(tools));

            // Convert to task-based.
            var finalCallToolHandler = callToolHandler;
            callToolWithTaskHandler = async (request, cancellationToken) =>
                await finalCallToolHandler(request, cancellationToken).ConfigureAwait(false);
        }

        // If a task store is configured, wrap so that when the client signals task support
        // the tool execution is offloaded to the background via the store.
        if (options.TaskStore is { } taskStore)
        {
            var innerTaskHandler = callToolWithTaskHandler;
            callToolWithTaskHandler = async (request, cancellationToken) =>
            {
                if (HasTaskExtensionOptIn(request.Params?.Meta))
                {
                    var taskInfo = await taskStore.CreateTaskAsync(cancellationToken).ConfigureAwait(false);
                    var taskId = taskInfo.TaskId;

                    var cts = new CancellationTokenSource();
                    _taskCancellationSources[taskId] = cts;

                    // Capture the token synchronously before Task.Run dispatches the work.
                    // The cancel handler may race with the background runner: whichever side wins
                    // the TryRemove call owns disposal. If we accessed cts.Token from inside the
                    // lambda after the handler had already disposed cts, we'd hit ObjectDisposedException.
                    var taskCancellationToken = cts.Token;

                    _ = Task.Run(async () =>
                    {
                        using (CreateMcpTaskScope(taskId, taskStore))
                        {
                            try
                            {
                                var augmented = await innerTaskHandler(request, taskCancellationToken).ConfigureAwait(false);
                                if (augmented.IsTask)
                                {
                                    // The handler created its own task externally, but the client already holds
                                    // the store's taskId from the synchronous return below — we can't redirect.
                                    // Fail the store's task so the client sees a clear error instead of polling forever.
                                    var error = new JsonRpcErrorDetail
                                    {
                                        Code = (int)McpErrorCode.InternalError,
                                        Message = $"{nameof(McpServerOptions.TaskStore)} is configured and the {nameof(McpServerHandlers.CallToolWithTaskHandler)} returned IsTask = true. Use only one mechanism to create the task.",
                                    };
                                    var errorJson = JsonSerializer.SerializeToElement(error, McpJsonUtilities.JsonContext.Default.JsonRpcErrorDetail);
                                    await taskStore.SetFailedAsync(taskId, errorJson).ConfigureAwait(false);
                                    return;
                                }

                                var resultJson = JsonSerializer.SerializeToElement(augmented.Result!, McpJsonUtilities.JsonContext.Default.CallToolResult);
                                await taskStore.SetCompletedAsync(taskId, resultJson).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (taskCancellationToken.IsCancellationRequested)
                            {
                                await taskStore.SetCancelledAsync(taskId, CancellationToken.None).ConfigureAwait(false);
                            }
                            catch (InputRequiredException)
                            {
                                // MRTR (input requests) cannot be composed with the task-store wrapper for
                                // [McpServerTool] methods today: the task ID was already returned synchronously,
                                // so we have no way to surface InputRequiredResult to the client retroactively.
                                // Fail the task with a clear, actionable error instead of leaking the raw
                                // InputRequiredException through the generic catch below.
                                var error = new JsonRpcErrorDetail
                                {
                                    Code = (int)McpErrorCode.InvalidRequest,
                                    Message = "MRTR (input requests) and tasks cannot be composed via [McpServerTool] yet; " +
                                              $"use {nameof(McpServerHandlers.CallToolWithTaskHandler)} to manage the input-request loop manually within the task body.",
                                };
                                var errorJson = JsonSerializer.SerializeToElement(error, McpJsonUtilities.JsonContext.Default.JsonRpcErrorDetail);
                                await taskStore.SetFailedAsync(taskId, errorJson).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                // SEP-2663 §186: failed.error MUST be a JSON-RPC error object {code, message, data?}.
                                // McpProtocolException carries a JSON-RPC ErrorCode and is documented as safe to
                                // propagate (Message + ErrorCode). For any other exception type, redact the message
                                // and use InternalError (mirrors the redaction in BuildInitialCallToolFilter).
                                var error = ex is McpProtocolException mcpEx
                                    ? new JsonRpcErrorDetail { Code = (int)mcpEx.ErrorCode, Message = mcpEx.Message }
                                    : new JsonRpcErrorDetail { Code = (int)McpErrorCode.InternalError, Message = "An error occurred while executing the task." };
                                var errorJson = JsonSerializer.SerializeToElement(error, McpJsonUtilities.JsonContext.Default.JsonRpcErrorDetail);
                                await taskStore.SetFailedAsync(taskId, errorJson).ConfigureAwait(false);
                            }
                            finally
                            {
                                // Only the side that wins TryRemove disposes cts. This prevents a
                                // double-dispose race with the default tasks/cancel handler.
                                if (_taskCancellationSources.TryRemove(taskId, out var registeredCts))
                                {
                                    registeredCts.Dispose();
                                }
                            }
                        }
                    }, CancellationToken.None);

                    return ToCreateTaskResult(taskInfo);
                }

                return await innerTaskHandler(request, cancellationToken).ConfigureAwait(false);
            };
        }

        ServerCapabilities.Tools.ListChanged = listChanged;

        SetHandler(
            RequestMethods.ToolsList,
            listToolsHandler,
            McpJsonUtilities.JsonContext.Default.ListToolsRequestParams,
            McpJsonUtilities.JsonContext.Default.ListToolsResult);

        SetTaskAugmentedHandler(
            RequestMethods.ToolsCall,
            callToolWithTaskHandler,
            McpJsonUtilities.JsonContext.Default.CallToolRequestParams,
            McpJsonUtilities.JsonContext.Default.CallToolResult,
            McpJsonUtilities.JsonContext.Default.CreateTaskResult);
    }

    private static CreateTaskResult ToCreateTaskResult(McpTaskInfo info) => new()
    {
        TaskId = info.TaskId,
        Status = info.Status,
        CreatedAt = info.CreatedAt,
        LastUpdatedAt = info.LastUpdatedAt,
        TtlMs = info.TtlMs,
        PollIntervalMs = info.PollIntervalMs,
        StatusMessage = info.StatusMessage,
        ResultType = "task",
    };

    private static GetTaskResult ToGetTaskResult(McpTaskInfo info) => info.Status switch
    {
        McpTaskStatus.Working => new WorkingTaskResult
        {
            TaskId = info.TaskId,
            CreatedAt = info.CreatedAt,
            LastUpdatedAt = info.LastUpdatedAt,
            TtlMs = info.TtlMs,
            PollIntervalMs = info.PollIntervalMs,
            StatusMessage = info.StatusMessage,
            ResultType = "complete",
        },
        McpTaskStatus.Completed => new CompletedTaskResult
        {
            TaskId = info.TaskId,
            CreatedAt = info.CreatedAt,
            LastUpdatedAt = info.LastUpdatedAt,
            TtlMs = info.TtlMs,
            PollIntervalMs = info.PollIntervalMs,
            StatusMessage = info.StatusMessage,
            Result = info.Result ?? throw new InvalidOperationException($"Task '{info.TaskId}' is completed but has no result."),
            ResultType = "complete",
        },
        McpTaskStatus.Failed => new FailedTaskResult
        {
            TaskId = info.TaskId,
            CreatedAt = info.CreatedAt,
            LastUpdatedAt = info.LastUpdatedAt,
            TtlMs = info.TtlMs,
            PollIntervalMs = info.PollIntervalMs,
            StatusMessage = info.StatusMessage,
            Error = info.Error ?? throw new InvalidOperationException($"Task '{info.TaskId}' is failed but has no error."),
            ResultType = "complete",
        },
        McpTaskStatus.Cancelled => new CancelledTaskResult
        {
            TaskId = info.TaskId,
            CreatedAt = info.CreatedAt,
            LastUpdatedAt = info.LastUpdatedAt,
            TtlMs = info.TtlMs,
            PollIntervalMs = info.PollIntervalMs,
            StatusMessage = info.StatusMessage,
            ResultType = "complete",
        },
        McpTaskStatus.InputRequired => new InputRequiredTaskResult
        {
            TaskId = info.TaskId,
            CreatedAt = info.CreatedAt,
            LastUpdatedAt = info.LastUpdatedAt,
            TtlMs = info.TtlMs,
            PollIntervalMs = info.PollIntervalMs,
            StatusMessage = info.StatusMessage,
            // McpTaskInfo.InputRequests is IReadOnlyDictionary (covers immutable store
            // implementations like InMemoryMcpTaskStore's ImmutableDictionary), while the wire
            // DTO uses IDictionary like every other Protocol type. Most concrete stores back
            // their dictionaries with a type that implements both interfaces (Dictionary,
            // ImmutableDictionary, ConcurrentDictionary), so the cast usually succeeds and we
            // only allocate a copy as a fallback.
            InputRequests = info.InputRequests is IDictionary<string, InputRequest> dict
                ? dict
                : info.InputRequests?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                    ?? new Dictionary<string, InputRequest>(),
            ResultType = "complete",
        },
        _ => throw new InvalidOperationException($"Unknown task status: {info.Status}"),
    };

    private static async ValueTask<ResultOrCreatedTask<CallToolResult>> InvokeToolAsTask(
        McpServerTool tool,
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken)
    {
        return await tool.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private McpRequestFilter<CallToolRequestParams, CallToolResult> BuildInitialCallToolFilter(
        McpServerPrimitiveCollection<McpServerTool>? tools) => handler =>
        async (request, cancellationToken) =>
        {
            if (request.Params?.Name is { } toolName && tools is not null &&
                tools.TryGetPrimitive(toolName, out var tool))
            {
                request.MatchedPrimitive = tool;
            }

            try
            {
                var result = await handler(request, cancellationToken).ConfigureAwait(false);
                ToolCallCompleted(request.Params?.Name ?? string.Empty, result.IsError is true);
                return result;
            }
            catch (Exception e)
            {
                // Skip logging for InputRequiredException - it's normal MRTR control flow,
                // not an error (tools throw it to signal an InputRequiredResult).
                if (!(e is OperationCanceledException && cancellationToken.IsCancellationRequested) && e is not InputRequiredException)
                {
                    ToolCallError(request.Params?.Name ?? string.Empty, e);
                }

                if ((e is OperationCanceledException && cancellationToken.IsCancellationRequested) || e is McpProtocolException || e is InputRequiredException)
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
        };

    private McpRequestFilter<CallToolRequestParams, ResultOrCreatedTask<CallToolResult>> BuildInitialTaskToolFilter(
        McpServerPrimitiveCollection<McpServerTool>? tools) => handler =>
        async (request, cancellationToken) =>
        {
            if (request.Params?.Name is { } toolName && tools is not null &&
                tools.TryGetPrimitive(toolName, out var tool))
            {
                request.MatchedPrimitive = tool;
            }

            try
            {
                var result = await handler(request, cancellationToken).ConfigureAwait(false);
                if (!result.IsTask)
                {
                    ToolCallCompleted(request.Params?.Name ?? string.Empty, result.Result!.IsError is true);
                }

                return result;
            }
            catch (Exception e)
            {
                // Skip logging for InputRequiredException - it's normal MRTR control flow,
                // not an error (tools throw it to signal an InputRequiredResult).
                if (!(e is OperationCanceledException && cancellationToken.IsCancellationRequested) && e is not InputRequiredException)
                {
                    ToolCallError(request.Params?.Name ?? string.Empty, e);
                }

                if ((e is OperationCanceledException && cancellationToken.IsCancellationRequested) || e is McpProtocolException || e is InputRequiredException)
                {
                    throw;
                }

                return new CallToolResult
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
        };

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
                    return InvokeHandlerAsync(setLoggingLevelHandler, request!, jsonRpcRequest, cancellationToken);
                }

                // Otherwise, consider it handled.
                return new ValueTask<EmptyResult>(EmptyResult.Instance);
            },
            McpJsonUtilities.JsonContext.Default.SetLevelRequestParams,
            McpJsonUtilities.JsonContext.Default.EmptyResult);
    }

    private ValueTask<TResult> InvokeHandlerAsync<TParams, TResult>(
        McpRequestHandler<TParams, TResult> handler,
        TParams args,
        JsonRpcRequest jsonRpcRequest,
        CancellationToken cancellationToken = default)
    {
        return _servicesScopePerRequest ?
            InvokeScopedAsync(handler, args, jsonRpcRequest, cancellationToken) :
            handler(new(CreateDestinationBoundServer(jsonRpcRequest), jsonRpcRequest, args), cancellationToken);

        async ValueTask<TResult> InvokeScopedAsync(
            McpRequestHandler<TParams, TResult> handler,
            TParams args,
            JsonRpcRequest jsonRpcRequest,
            CancellationToken cancellationToken)
        {
            var scope = Services?.GetService<IServiceScopeFactory>()?.CreateAsyncScope();
            try
            {
                return await handler(
                    new RequestContext<TParams>(CreateDestinationBoundServer(jsonRpcRequest), jsonRpcRequest, args)
                    {
                        Services = scope?.ServiceProvider ?? Services,
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

    private DestinationBoundMcpServer CreateDestinationBoundServer(JsonRpcRequest jsonRpcRequest)
    {
        var server = new DestinationBoundMcpServer(this, jsonRpcRequest.Context?.RelatedTransport);

        if (_mrtrContextsByRequestId.TryRemove(jsonRpcRequest.Id, out var mrtrContext))
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
        // SEP-2549: results that carry caching hints (tools/list, prompts/list, resources/list,
        // resources/templates/list, and resources/read) declare ttlMs and cacheScope as required fields.
        // When a handler leaves them unset, fill in conservative defaults (immediately stale and not
        // shareable) so the wire form always carries the fields while preserving today's "don't cache"
        // behavior. Any value supplied by the handler or a filter is left untouched.
        if (typeof(ICacheableResult).IsAssignableFrom(typeof(TResult)))
        {
            var innerHandler = handler;
            handler = async (request, cancellationToken) =>
            {
                var result = await innerHandler(request, cancellationToken).ConfigureAwait(false);
                if (result is ICacheableResult cacheable)
                {
                    cacheable.TimeToLive ??= TimeSpan.Zero;
                    cacheable.CacheScope ??= CacheScope.Private;
                }

                return result;
            };
        }

        _requestHandlers.Set(method,
            (request, jsonRpcRequest, cancellationToken) =>
                InvokeHandlerAsync(handler, request, jsonRpcRequest, cancellationToken),
            requestTypeInfo, responseTypeInfo);
    }

    private void SetTaskAugmentedHandler<TParams, TResult>(
        string method,
        McpRequestHandler<TParams, ResultOrCreatedTask<TResult>> handler,
        JsonTypeInfo<TParams> requestTypeInfo,
        JsonTypeInfo<TResult> responseTypeInfo,
        JsonTypeInfo<CreateTaskResult> taskResultTypeInfo)
        where TResult : Result
    {
        _requestHandlers.SetTaskAugmented(method,
            (request, jsonRpcRequest, cancellationToken) =>
                InvokeHandlerAsync(handler, request, jsonRpcRequest, cancellationToken),
            requestTypeInfo, responseTypeInfo, taskResultTypeInfo);
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

    // Per SEP-2663 §51, the client opts in to the tasks extension on a per-request basis
    // via the SEP-2575 capabilities envelope:
    //   _meta/io.modelcontextprotocol/clientCapabilities/extensions/io.modelcontextprotocol/tasks = {}
    // TODO: swap the literals for a shared NotificationMethods.ClientCapabilitiesMetaKey once
    // the SEP-2575 plumbing lands.
    private const string ClientCapabilitiesMetaKey = "io.modelcontextprotocol/clientCapabilities";
    private const string ExtensionsKey = "extensions";

    private static bool HasTaskExtensionOptIn(JsonObject? meta) =>
        meta is not null &&
        meta[ClientCapabilitiesMetaKey] is JsonObject caps &&
        caps[ExtensionsKey] is JsonObject exts &&
        exts.ContainsKey(McpExtensions.Tasks);

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
    /// Checks whether the negotiated protocol version enables MRTR per SEP-2322 (DRAFT-2026-v1).
    /// </summary>
    internal bool ClientSupportsMrtr() =>
        _negotiatedProtocolVersion == McpSessionHandler.DraftProtocolVersion;

    /// <summary>
    /// Returns <see langword="true"/> when the session is stateful - the same server instance handles
    /// subsequent requests on the same session. The legacy backcompat resolver in
    /// <see cref="InvokeWithInputRequiredResultHandlingAsync"/> needs a stateful session so it can send
    /// <c>elicitation/create</c> / <c>sampling/createMessage</c> / <c>roots/list</c> to the client and
    /// retry the handler with the responses.
    /// </summary>
    internal bool IsStatefulSession() =>
        _sessionTransport is not StreamableHttpServerTransport { Stateless: true };

    /// <inheritdoc />
    public override bool IsMrtrSupported => ClientSupportsMrtr() || IsStatefulSession();

    /// <summary>
    /// Invokes a handler and catches <see cref="InputRequiredException"/> to convert it to an
    /// <see cref="InputRequiredResult"/> JSON response. When MRTR is negotiated or the server is stateless,
    /// the result is serialized directly. Otherwise, input requests are resolved via standard JSON-RPC
    /// calls (elicitation, sampling, roots) and the handler is retried with the responses - allowing
    /// MRTR-native tools to work transparently with clients that don't support MRTR.
    /// </summary>
    private async Task<JsonNode?> InvokeWithInputRequiredResultHandlingAsync(
        Func<JsonRpcRequest, CancellationToken, Task<JsonNode?>> handler,
        JsonRpcRequest request,
        CancellationToken cancellationToken)
    {
        const int MaxRetries = 10;

        // In stateless mode, pick up the negotiated draft protocol version from the
        // transport-provided request context because there is no long-lived initialize handshake state.
        if (_negotiatedProtocolVersion is null &&
            request.Context?.ProtocolVersion is { } headerProtocolVersion)
        {
            _negotiatedProtocolVersion = headerProtocolVersion;
        }

        for (int retry = 0; ; retry++)
        {
            try
            {
                return await handler(request, cancellationToken).ConfigureAwait(false);
            }
            catch (InputRequiredException ex)
            {
                // If the client natively supports MRTR, serialize and return directly -
                // the client will drive the retry loop.
                if (ClientSupportsMrtr())
                {
                    return SerializeInputRequiredResult(ex.Result);
                }

                // In stateless mode without MRTR, the server can't resolve input requests via
                // JSON-RPC (no persistent session for server-to-client requests), and the client
                // won't recognize the InputRequiredResult. This is the one unsupported configuration.
                // TODO(stateless-draft): When DRAFT-2026-v1 becomes stateless-only, the IsStatefulSession() gate collapses - the stateful path will only matter for legacy clients on the current protocol.
                if (!IsStatefulSession())
                {
                    throw new McpException(
                        "A tool handler returned an incomplete result, but the server is stateless and the client does not support MRTR. " +
                        "MRTR-native tools require either an MRTR-capable client or a stateful server for backward-compatible resolution.", ex);
                }

                // Backcompat: resolve input requests via standard JSON-RPC calls and retry the handler.
                if (ex.Result.InputRequests is not { Count: > 0 } inputRequests)
                {
                    throw new McpException(
                        "A tool handler returned an incomplete result without input requests, and the client does not support MRTR.", ex);
                }

                if (retry >= MaxRetries)
                {
                    throw new McpException(
                        $"MRTR-native tool exceeded {MaxRetries} retry rounds without completing.", ex);
                }

                // Resolve each input request by sending the corresponding JSON-RPC call to the client.
                // Route the outgoing requests via the same DestinationBoundMcpServer used for normal tool
                // handlers, so they go through the POST's response stream (RelatedTransport) rather than
                // the session-level transport. Without this, the messages can race with the client's GET
                // stream startup and be silently dropped by StreamableHttpServerTransport.SendMessageAsync
                // when no GET request has arrived yet.
                var destinationServer = CreateDestinationBoundServer(request);
                var inputResponses = await ResolveInputRequestsAsync(destinationServer, inputRequests, cancellationToken).ConfigureAwait(false);

                // Reconstruct request params with inputResponses and requestState for the retry.
                var paramsObj = request.Params?.DeepClone() as JsonObject ?? new JsonObject();
                paramsObj["inputResponses"] = JsonSerializer.SerializeToNode(
                    (IDictionary<string, InputResponse>)inputResponses, McpJsonUtilities.JsonContext.Default.IDictionaryStringInputResponse);

                if (ex.Result.RequestState is { } requestState)
                {
                    paramsObj["requestState"] = requestState;
                }
                else
                {
                    // Strip any stale requestState carried over from the previous round's clone so
                    // the next tool invocation doesn't see a continuation token the current round is not using.
                    paramsObj.Remove("requestState");
                }

                request = new JsonRpcRequest
                {
                    Id = request.Id,
                    Method = request.Method,
                    Params = paramsObj,
                    Context = request.Context,
                };
            }
        }
    }

    /// <summary>
    /// Resolves a batch of MRTR input requests concurrently by dispatching each as a standard
    /// JSON-RPC request to the client. The requests are routed via <paramref name="destinationServer"/>
    /// so they go out through the POST's response stream (matching the behavior of tool-initiated
    /// server-to-client requests like <c>server.SampleAsync</c>) and avoid racing with the client's
    /// GET stream startup. On the first failure all remaining handlers are cancelled so user-facing
    /// flows (sampling/elicitation prompts) don't keep running once the caller has given up, and
    /// exceptions from late-completing tasks are observed before the original exception is rethrown.
    /// </summary>
    private static async Task<IDictionary<string, InputResponse>> ResolveInputRequestsAsync(
        McpServer destinationServer,
        IDictionary<string, InputRequest> inputRequests,
        CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var keyed = new (string Key, Task<InputResponse> Task)[inputRequests.Count];
        int i = 0;
        foreach (var kvp in inputRequests)
        {
            keyed[i++] = (kvp.Key, ResolveInputRequestAsync(destinationServer, kvp.Value, linkedCts.Token));
        }

        try
        {
            await Task.WhenAll(Array.ConvertAll(keyed, k => k.Task)).ConfigureAwait(false);
        }
        catch
        {
            linkedCts.Cancel();
            try
            {
                await Task.WhenAll(Array.ConvertAll(keyed, k => k.Task)).ConfigureAwait(false);
            }
            catch
            {
                // Observed; the original exception is the one we want to surface.
            }
            throw;
        }

        var responses = new Dictionary<string, InputResponse>(keyed.Length);
        foreach (var (key, task) in keyed)
        {
            responses[key] = task.Result;
        }
        return responses;
    }

    /// <summary>
    /// Resolves a single MRTR <see cref="InputRequest"/> by dispatching it as a standard JSON-RPC
    /// request to the client via <paramref name="destinationServer"/>. This is the server-side mirror
    /// of the client's input resolution logic, used for backward compatibility when the client doesn't
    /// support MRTR.
    /// </summary>
    private static async Task<InputResponse> ResolveInputRequestAsync(McpServer destinationServer, InputRequest inputRequest, CancellationToken cancellationToken)
    {
        switch (inputRequest.Method)
        {
            case RequestMethods.ElicitationCreate:
                var elicitParams = inputRequest.ElicitationParams
                    ?? throw new McpException("Failed to deserialize elicitation parameters from MRTR input request.");
                var elicitResult = await destinationServer.ElicitAsync(elicitParams, cancellationToken).ConfigureAwait(false);
                return InputResponse.FromElicitResult(elicitResult);

            case RequestMethods.SamplingCreateMessage:
                var samplingParams = inputRequest.SamplingParams
                    ?? throw new McpException("Failed to deserialize sampling parameters from MRTR input request.");
                var samplingResult = await destinationServer.SampleAsync(samplingParams, cancellationToken).ConfigureAwait(false);
                return InputResponse.FromSamplingResult(samplingResult);

            case RequestMethods.RootsList:
                var rootsParams = inputRequest.RootsParams ?? new ListRootsRequestParams();
                var rootsResult = await destinationServer.RequestRootsAsync(rootsParams, cancellationToken).ConfigureAwait(false);
                return InputResponse.FromRootsResult(rootsResult);

            default:
                throw new McpException($"Unsupported input request method: '{inputRequest.Method}'.");
        }
    }

    private static JsonNode? SerializeInputRequiredResult(InputRequiredResult inputRequiredResult) =>
        JsonSerializer.SerializeToNode(inputRequiredResult, McpJsonUtilities.JsonContext.Default.InputRequiredResult);

    /// <summary>
    /// Wraps MRTR-eligible request handlers so that when a handler calls ElicitAsync/SampleAsync/RequestRootsAsync,
    /// an <see cref="InputRequiredResult"/> is returned early and the handler is suspended until the retry arrives.
    /// </summary>
    private void ConfigureMrtr()
    {
        // Wrap all methods that may trigger MRTR (server calling ElicitAsync/SampleAsync/RequestRootsAsync
        // during handler execution). These methods may produce InputRequiredResult if the handler needs input.
        WrapHandlerWithMrtr(RequestMethods.ToolsCall);
        WrapHandlerWithMrtr(RequestMethods.PromptsGet);
        WrapHandlerWithMrtr(RequestMethods.ResourcesRead);
    }

    /// <summary>
    /// Replaces an existing request handler entry with an MRTR-aware wrapper that supports
    /// handler suspension and <see cref="InputRequiredResult"/> responses.
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
                    // Implicit MRTR retry: resume the suspended handler with client responses.
                    IDictionary<string, InputResponse>? inputResponses = null;
                    if (paramsObj.TryGetPropertyValue("inputResponses", out var responsesNode) && responsesNode is not null)
                    {
                        inputResponses = JsonSerializer.Deserialize(responsesNode, McpJsonUtilities.JsonContext.Default.IDictionaryStringInputResponse);
                    }

                    var exchange = existingContinuation.PendingExchange!;
                    var nextExchangeTask = existingContinuation.MrtrContext.ResetForNextExchange(exchange);

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

                // Explicit MRTR retry or invalid requestState: no continuation found.
                // Fall through to the standard MRTR-aware invocation path below. The retry data
                // (inputResponses, requestState) is already in the deserialized request params
                // for low-level handlers to access, and the MrtrContext will be set up for
                // high-level handlers that call ElicitAsync/SampleAsync.
            }

            // Implicit MRTR (handler suspension across ElicitAsync/SampleAsync) emits
            // InputRequiredResult on the wire, which only DRAFT-2026-v1 clients understand,
            // and requires the same server instance to handle the retry (stateful session).
            // For all other cases - legacy clients, stateless sessions - fall through to the
            // exception-based path, which transparently resolves InputRequiredException via
            // legacy JSON-RPC requests when the client doesn't speak MRTR.
            if (!ClientSupportsMrtr() || !IsStatefulSession())
            {
                return await InvokeWithInputRequiredResultHandlingAsync(originalHandler, request, cancellationToken).ConfigureAwait(false);
            }

            // Start a new MRTR-aware handler invocation.
            var mrtrContext = new MrtrContext();

            // Create a long-lived CTS for the handler that survives across retries.
            // The original request's combinedCts will be disposed when this lambda returns,
            // breaking the cancellation chain. This CTS keeps the handler cancellable.
            // Like Kestrel's HttpContext.RequestAborted, the CTS is never disposed - Cancel()
            // is thread-safe with itself, and not disposing avoids deadlock risks from
            // calling Cancel/Dispose inside locks or Interlocked guards.
            var handlerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Store the MrtrContext so CreateDestinationBoundServer can pick it up and set it
            // on the per-request DestinationBoundMcpServer. This is picked up synchronously
            // before any await, so the finally cleanup is safe.
            _mrtrContextsByRequestId[request.Id] = mrtrContext;
            Task<JsonNode?> handlerTask;
            try
            {
                handlerTask = originalHandler(request, handlerCts.Token);
            }
            finally
            {
                _mrtrContextsByRequestId.TryRemove(request.Id, out _);
            }

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
    /// Awaits the outcome of an MRTR-enabled handler invocation.
    /// If the handler completes, returns its result. If an exchange arrives (handler needs input),
    /// builds and returns an <see cref="InputRequiredResult"/> and stores the continuation for future retries.
    /// If the handler throws <see cref="InputRequiredException"/>, the result is returned directly
    /// without storing a continuation (explicit MRTR path).
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
        using var registration = cancellationToken.Register(
            static state => ((MrtrContinuation)state!).CancelHandler(), continuation);

        // Race handler against MRTR exchange.
        var completedTask = await Task.WhenAny(handlerTask, exchangeTask).ConfigureAwait(false);

        if (completedTask == handlerTask)
        {
            // Handler completed - return its result, propagate its exception, or handle InputRequiredException.
            return await AwaitHandlerWithInputRequiredResultHandlingAsync(handlerTask).ConfigureAwait(false);
        }

        // Exchange arrived - handler needs input from the client (implicit MRTR path).
        var exchange = await exchangeTask.ConfigureAwait(false);

        var correlationId = Guid.NewGuid().ToString("N");
        var inputRequiredResult = new InputRequiredResult
        {
            InputRequests = new Dictionary<string, InputRequest> { [exchange.Key] = exchange.InputRequest },
            RequestState = correlationId,
        };

        // Store the continuation so the retry can resume the handler.
        continuation.PendingExchange = exchange;
        _mrtrContinuations[correlationId] = continuation;

        return SerializeInputRequiredResult(inputRequiredResult);
    }

    /// <summary>
    /// Fire-and-forget observer for an MRTR handler task. Logs unhandled exceptions at Debug
    /// level (the same exception still propagates to the request pipeline, so Debug avoids
    /// double-reporting at Error) and decrements <see cref="_mrtrInFlightCount"/> when the
    /// handler completes, following the same in-flight tracking pattern as <see cref="McpSessionHandler"/>.
    /// </summary>
    private async Task ObserveHandlerCompletionAsync(Task<JsonNode?> handlerTask)
    {
        try
        {
            await handlerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Handler cancelled - expected lifecycle event (disposal, client cancel, session shutdown).
        }
        catch (InputRequiredException)
        {
            // Explicit MRTR: handler explicitly signaling an InputRequiredResult. Not an error.
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
    /// Awaits a handler task, catching <see cref="InputRequiredException"/> to convert it to an
    /// <see cref="InputRequiredResult"/> JSON response without storing a continuation.
    /// </summary>
    private static async Task<JsonNode?> AwaitHandlerWithInputRequiredResultHandlingAsync(Task<JsonNode?> handlerTask)
    {
        try
        {
            return await handlerTask.ConfigureAwait(false);
        }
        catch (InputRequiredException ex)
        {
            return SerializeInputRequiredResult(ex.Result);
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "An MRTR handler threw an unhandled exception.")]
    private partial void MrtrHandlerError(Exception exception);
}
