using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Server;

/// <inheritdoc />
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
    {
        Throw.IfNull(transport);
        Throw.IfNull(options);

        options ??= new();

        _sessionTransport = transport;
        ServerOptions = options;
        Services = serviceProvider;
        _serverOnlyEndpointName = $"Server ({options.ServerInfo?.Name ?? DefaultImplementation.Name} {options.ServerInfo?.Version ?? DefaultImplementation.Version})";
        _endpointName = _serverOnlyEndpointName;
        _servicesScopePerRequest = options.ScopeRequests;
        _logger = loggerFactory?.CreateLogger<McpServer>() ?? NullLogger<McpServer>.Instance;

        _clientInfo = options.KnownClientInfo;
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
        ConfigureExperimental(options);
        ConfigurePing();

        // Register any notification handlers that were provided.
        if (options.Capabilities?.NotificationHandlers is { } notificationHandlers)
        {
            _notificationHandlers.RegisterRange(notificationHandlers);
        }

        // Now that everything has been configured, subscribe to any necessary notifications.
        if (transport is not StreamableHttpServerTransport streamableHttpTransport || streamableHttpTransport.Stateless is false)
        {
            Register(ServerOptions.Capabilities?.Tools?.ToolCollection, NotificationMethods.ToolListChangedNotification);
            Register(ServerOptions.Capabilities?.Prompts?.PromptCollection, NotificationMethods.PromptListChangedNotification);
            Register(ServerOptions.Capabilities?.Resources?.ResourceCollection, NotificationMethods.ResourceListChangedNotification);

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
        _sessionHandler = new McpSessionHandler(isServer: true, _sessionTransport, _endpointName!, _requestHandlers, _notificationHandlers, _logger);
    }

    /// <inheritdoc/>
    public override string? SessionId => _sessionTransport.SessionId;

    /// <inheritdoc/>
    public override string? NegotiatedProtocolVersion => _negotiatedProtocolVersion;

    /// <inheritdoc/>
    public ServerCapabilities ServerCapabilities { get; } = new();

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

    private void ConfigurePing()
    {
        SetHandler(RequestMethods.Ping,
            async (request, _) => new PingResult(),
            McpJsonUtilities.JsonContext.Default.JsonNode,
            McpJsonUtilities.JsonContext.Default.PingResult);
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
                if (protocolVersion is null)
                {
                    protocolVersion = request?.ProtocolVersion is string clientProtocolVersion && McpSessionHandler.SupportedProtocolVersions.Contains(clientProtocolVersion) ?
                        clientProtocolVersion :
                        McpSessionHandler.LatestProtocolVersion;
                }

                _negotiatedProtocolVersion = protocolVersion;

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
        if (options.Capabilities?.Completions is not { } completionsCapability)
        {
            return;
        }

        var completeHandler = completionsCapability.CompleteHandler ?? (static async (_, __) => new CompleteResult());
        completeHandler = BuildFilterPipeline(completeHandler, options.Filters.CompleteFilters);

        ServerCapabilities.Completions = new()
        {
            CompleteHandler = completeHandler
        };

        SetHandler(
            RequestMethods.CompletionComplete,
            ServerCapabilities.Completions.CompleteHandler,
            McpJsonUtilities.JsonContext.Default.CompleteRequestParams,
            McpJsonUtilities.JsonContext.Default.CompleteResult);
    }

    private void ConfigureExperimental(McpServerOptions options)
    {
        ServerCapabilities.Experimental = options.Capabilities?.Experimental;
    }

    private void ConfigureResources(McpServerOptions options)
    {
        if (options.Capabilities?.Resources is not { } resourcesCapability)
        {
            return;
        }

        ServerCapabilities.Resources = new();

        var listResourcesHandler = resourcesCapability.ListResourcesHandler ?? (static async (_, __) => new ListResourcesResult());
        var listResourceTemplatesHandler = resourcesCapability.ListResourceTemplatesHandler ?? (static async (_, __) => new ListResourceTemplatesResult());
        var readResourceHandler = resourcesCapability.ReadResourceHandler ?? (static async (request, _) => throw new McpException($"Unknown resource URI: '{request.Params?.Uri}'", McpErrorCode.InvalidParams));
        var subscribeHandler = resourcesCapability.SubscribeToResourcesHandler ?? (static async (_, __) => new EmptyResult());
        var unsubscribeHandler = resourcesCapability.UnsubscribeFromResourcesHandler ?? (static async (_, __) => new EmptyResult());
        var resources = resourcesCapability.ResourceCollection;
        var listChanged = resourcesCapability.ListChanged;
        var subscribe = resourcesCapability.Subscribe;

        // Handle resources provided via DI.
        if (resources is { IsEmpty: false })
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
                    if (await matchedResource.ReadAsync(request, cancellationToken).ConfigureAwait(false) is { } result)
                    {
                        return result;
                    }
                }

                return await originalReadResourceHandler(request, cancellationToken).ConfigureAwait(false);
            };

            listChanged = true;

            // TODO: Implement subscribe/unsubscribe logic for resource and resource template collections.
            // subscribe = true;
        }

        listResourcesHandler = BuildFilterPipeline(listResourcesHandler, options.Filters.ListResourcesFilters);
        listResourceTemplatesHandler = BuildFilterPipeline(listResourceTemplatesHandler, options.Filters.ListResourceTemplatesFilters);
        readResourceHandler = BuildFilterPipeline(readResourceHandler, options.Filters.ReadResourceFilters, handler =>
            async (request, cancellationToken) =>
            {
                // Initial handler that sets MatchedPrimitive
                if (request.Params?.Uri is { } uri && resources is not null)
                {
                    // First try an O(1) lookup by exact match.
                    if (resources.TryGetPrimitive(uri, out var resource))
                    {
                        request.MatchedPrimitive = resource;
                    }
                    else
                    {
                        // Fall back to an O(N) lookup, trying to match against each URI template.
                        // The number of templates is controlled by the server developer, and the number is expected to be
                        // not terribly large. If that changes, this can be tweaked to enable a more efficient lookup.
                        foreach (var resourceTemplate in resources)
                        {
                            // Check if this template would handle the request by testing if ReadAsync would succeed
                            if (resourceTemplate.IsTemplated)
                            {
                                // This is a simplified check - a more robust implementation would match the URI pattern
                                // For now, we'll let the actual handler attempt the match
                                request.MatchedPrimitive = resourceTemplate;
                                break;
                            }
                        }
                    }
                }

                return await handler(request, cancellationToken).ConfigureAwait(false);
            });
        subscribeHandler = BuildFilterPipeline(subscribeHandler, options.Filters.SubscribeToResourcesFilters);
        unsubscribeHandler = BuildFilterPipeline(unsubscribeHandler, options.Filters.UnsubscribeFromResourcesFilters);

        ServerCapabilities.Resources.ListResourcesHandler = listResourcesHandler;
        ServerCapabilities.Resources.ListResourceTemplatesHandler = listResourceTemplatesHandler;
        ServerCapabilities.Resources.ReadResourceHandler = readResourceHandler;
        ServerCapabilities.Resources.ResourceCollection = resources;
        ServerCapabilities.Resources.SubscribeToResourcesHandler = subscribeHandler;
        ServerCapabilities.Resources.UnsubscribeFromResourcesHandler = unsubscribeHandler;
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
        if (options.Capabilities?.Prompts is not { } promptsCapability)
        {
            return;
        }

        ServerCapabilities.Prompts = new();

        var listPromptsHandler = promptsCapability.ListPromptsHandler ?? (static async (_, __) => new ListPromptsResult());
        var getPromptHandler = promptsCapability.GetPromptHandler ?? (static async (request, _) => throw new McpException($"Unknown prompt: '{request.Params?.Name}'", McpErrorCode.InvalidParams));
        var prompts = promptsCapability.PromptCollection;
        var listChanged = promptsCapability.ListChanged;

        // Handle tools provided via DI by augmenting the handlers to incorporate them.
        if (prompts is { IsEmpty: false })
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

        listPromptsHandler = BuildFilterPipeline(listPromptsHandler, options.Filters.ListPromptsFilters);
        getPromptHandler = BuildFilterPipeline(getPromptHandler, options.Filters.GetPromptFilters, handler =>
            (request, cancellationToken) =>
            {
                // Initial handler that sets MatchedPrimitive
                if (request.Params?.Name is { } promptName && prompts is not null &&
                    prompts.TryGetPrimitive(promptName, out var prompt))
                {
                    request.MatchedPrimitive = prompt;
                }

                return handler(request, cancellationToken);
            });

        ServerCapabilities.Prompts.ListPromptsHandler = listPromptsHandler;
        ServerCapabilities.Prompts.GetPromptHandler = getPromptHandler;
        ServerCapabilities.Prompts.PromptCollection = prompts;
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
        if (options.Capabilities?.Tools is not { } toolsCapability)
        {
            return;
        }

        ServerCapabilities.Tools = new();

        var listToolsHandler = toolsCapability.ListToolsHandler ?? (static async (_, __) => new ListToolsResult());
        var callToolHandler = toolsCapability.CallToolHandler ?? (static async (request, _) => throw new McpException($"Unknown tool: '{request.Params?.Name}'", McpErrorCode.InvalidParams));
        var tools = toolsCapability.ToolCollection;
        var listChanged = toolsCapability.ListChanged;

        // Handle tools provided via DI by augmenting the handlers to incorporate them.
        if (tools is { IsEmpty: false })
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

        listToolsHandler = BuildFilterPipeline(listToolsHandler, options.Filters.ListToolsFilters);
        callToolHandler = BuildFilterPipeline(callToolHandler, options.Filters.CallToolFilters, handler =>
            (request, cancellationToken) =>
            {
                // Initial handler that sets MatchedPrimitive
                if (request.Params?.Name is { } toolName && tools is not null &&
                    tools.TryGetPrimitive(toolName, out var tool))
                {
                    request.MatchedPrimitive = tool;
                }

                return handler(request, cancellationToken);
            }, handler =>
            async (request, cancellationToken) =>
            {
                // Final handler that provides exception handling only for tool execution
                // Only wrap tool execution in try-catch, not tool resolution
                if (request.MatchedPrimitive is McpServerTool)
                {
                    try
                    {
                        return await handler(request, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        ToolCallError(request.Params?.Name ?? string.Empty, e);

                        string errorMessage = e is McpException ?
                            $"An error occurred invoking '{request.Params?.Name}': {e.Message}" :
                            $"An error occurred invoking '{request.Params?.Name}'.";

                        return new()
                        {
                            IsError = true,
                            Content = [new TextContentBlock { Text = errorMessage }],
                        };
                    }
                }
                else
                {
                    // For unmatched tools, let exceptions bubble up as protocol errors
                    return await handler(request, cancellationToken).ConfigureAwait(false);
                }
            });

        ServerCapabilities.Tools.ListToolsHandler = listToolsHandler;
        ServerCapabilities.Tools.CallToolHandler = callToolHandler;
        ServerCapabilities.Tools.ToolCollection = tools;
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
        var setLoggingLevelHandler = options.Capabilities?.Logging?.SetLoggingLevelHandler;

        // Apply filters to the handler
        if (setLoggingLevelHandler is not null)
        {
            setLoggingLevelHandler = BuildFilterPipeline(setLoggingLevelHandler, options.Filters.SetLoggingLevelFilters);
        }

        ServerCapabilities.Logging = new();
        ServerCapabilities.Logging.SetLoggingLevelHandler = setLoggingLevelHandler;

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
            handler(new(new DestinationBoundMcpServer(this, jsonRpcRequest.Context?.RelatedTransport), jsonRpcRequest) { Params = args }, cancellationToken);

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
                    new RequestContext<TParams>(new DestinationBoundMcpServer(this, jsonRpcRequest.Context?.RelatedTransport), jsonRpcRequest)
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
        List<McpRequestFilter<TParams, TResult>> filters,
        McpRequestFilter<TParams, TResult>? initialHandler = null,
        McpRequestFilter<TParams, TResult>? finalHandler = null)
    {
        var current = baseHandler;

        if (finalHandler is not null)
        {
            current = finalHandler(current);
        }

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
}
