using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using System.Runtime.CompilerServices;
using System.Text.Json;
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

        _disposables.ForEach(d => d());
        await _sessionHandler.DisposeAsync().ConfigureAwait(false);
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
                protocolVersion ??= request?.ProtocolVersion is string clientProtocolVersion && McpSessionHandler.SupportedProtocolVersions.Contains(clientProtocolVersion) ?
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
            callToolHandler = (request, cancellationToken) =>
            {
                if (request.MatchedPrimitive is McpServerTool tool)
                {
                    return tool.InvokeAsync(request, cancellationToken);
                }

                return originalCallToolHandler(request, cancellationToken);
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
                    ToolCallCompleted(request.Params?.Name ?? string.Empty, result.IsError is true);
                    return result;
                }
                catch (Exception e)
                {
                    ToolCallError(request.Params?.Name ?? string.Empty, e);

                    if ((e is OperationCanceledException && cancellationToken.IsCancellationRequested) || e is McpProtocolException)
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
            handler(new(new DestinationBoundMcpServer(this, jsonRpcRequest.Context?.RelatedTransport), jsonRpcRequest, args), cancellationToken);

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
                    new RequestContext<TParams>(new DestinationBoundMcpServer(this, jsonRpcRequest.Context?.RelatedTransport), jsonRpcRequest, args)
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
}
