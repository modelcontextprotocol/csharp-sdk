using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Server;

/// <inheritdoc />
internal sealed class McpServer : McpEndpoint, IMcpServer
{
    internal static Implementation DefaultImplementation { get; } = new()
    {
        Name = DefaultAssemblyName.Name ?? nameof(McpServer),
        Version = DefaultAssemblyName.Version?.ToString() ?? "1.0.0",
    };

    private readonly ITransport _sessionTransport;
    private readonly bool _servicesScopePerRequest;
    private readonly List<Action> _disposables = [];

    private readonly string _serverOnlyEndpointName;
    private string? _endpointName;
    private int _started;

    /// <summary>Holds a boxed <see cref="LoggingLevel"/> value for the server.</summary>
    /// <remarks>
    /// Initialized to non-null the first time SetLevel is used. This is stored as a strong box
    /// rather than a nullable to be able to manipulate it atomically.
    /// </remarks>
    private StrongBox<LoggingLevel>? _loggingLevel;

    /// <summary>
    /// Creates a new instance of <see cref="McpServer"/>.
    /// </summary>
    /// <param name="transport">Transport to use for the server representing an already-established session.</param>
    /// <param name="options">Configuration options for this server, including capabilities.
    /// Make sure to accurately reflect exactly what capabilities the server supports and does not support.</param>
    /// <param name="loggerFactory">Logger factory to use for logging</param>
    /// <param name="serviceProvider">Optional service provider to use for dependency injection</param>
    /// <exception cref="McpException">The server was incorrectly configured.</exception>
    public McpServer(ITransport transport, McpServerOptions options, ILoggerFactory? loggerFactory, IServiceProvider? serviceProvider)
        : base(loggerFactory)
    {
        Throw.IfNull(transport);
        Throw.IfNull(options);

        options ??= new();

        _sessionTransport = transport;
        ServerOptions = options;
        Services = serviceProvider;
        _serverOnlyEndpointName = $"Server ({options.ServerInfo?.Name ?? DefaultImplementation.Name} {options.ServerInfo?.Version ?? DefaultImplementation.Version})";
        _servicesScopePerRequest = options.ScopeRequests;

        ClientInfo = options.KnownClientInfo;
        UpdateEndpointNameWithClientInfo();

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
            NotificationHandlers.RegisterRange(notificationHandlers);
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
        InitializeSession(transport);
    }

    /// <inheritdoc/>
    public string? SessionId => _sessionTransport.SessionId;

    /// <inheritdoc/>
    public ServerCapabilities ServerCapabilities { get; } = new();

    /// <inheritdoc />
    public ClientCapabilities? ClientCapabilities { get; set; }

    /// <inheritdoc />
    public Implementation? ClientInfo { get; set; }

    /// <inheritdoc />
    public McpServerOptions ServerOptions { get; }

    /// <inheritdoc />
    public IServiceProvider? Services { get; }

    /// <inheritdoc />
    public override string EndpointName => _endpointName ?? _serverOnlyEndpointName;

    /// <inheritdoc />
    public LoggingLevel? LoggingLevel => _loggingLevel?.Value;

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException($"{nameof(RunAsync)} must only be called once.");
        }

        try
        {
            StartSession(_sessionTransport, fullSessionCancellationToken: cancellationToken);
            await MessageProcessingTask.ConfigureAwait(false);
        }
        finally
        {
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    public override async ValueTask DisposeUnsynchronizedAsync()
    {
        _disposables.ForEach(d => d());
        await base.DisposeUnsynchronizedAsync().ConfigureAwait(false);
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
        RequestHandlers.Set(RequestMethods.Initialize,
            async (request, _, _) =>
            {
                ClientCapabilities = request?.Capabilities ?? new();
                ClientInfo = request?.ClientInfo;

                // Use the ClientInfo to update the session EndpointName for logging.
                UpdateEndpointNameWithClientInfo();
                GetSessionOrThrow().EndpointName = EndpointName;

                // Negotiate a protocol version. If the server options provide one, use that.
                // Otherwise, try to use whatever the client requested as long as it's supported.
                // If it's not supported, fall back to the latest supported version.
                string? protocolVersion = options.ProtocolVersion;
                if (protocolVersion is null)
                {
                    protocolVersion = request?.ProtocolVersion is string clientProtocolVersion && McpSession.SupportedProtocolVersions.Contains(clientProtocolVersion) ?
                        clientProtocolVersion :
                        McpSession.LatestProtocolVersion;
                }

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

        ServerCapabilities.Completions = new()
        {
            CompleteHandler = completionsCapability.CompleteHandler ?? (static async (_, __) => new CompleteResult())
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
                if (request.Params?.Uri is string uri)
                {
                    // First try an O(1) lookup by exact match.
                    if (resources.TryGetPrimitive(uri, out var resource))
                    {
                        if (await resource.ReadAsync(request, cancellationToken).ConfigureAwait(false) is { } result)
                        {
                            return result;
                        }
                    }

                    // Fall back to an O(N) lookup, trying to match against each URI template.
                    // The number of templates is controlled by the server developer, and the number is expected to be
                    // not terribly large. If that changes, this can be tweaked to enable a more efficient lookup.
                    foreach (var resourceTemplate in resources)
                    {
                        if (await resourceTemplate.ReadAsync(request, cancellationToken).ConfigureAwait(false) is { } result)
                        {
                            return result;
                        }
                    }
                }

                // Finally fall back to the handler.
                return await originalReadResourceHandler(request, cancellationToken).ConfigureAwait(false);
            };

            listChanged = true;

            // TODO: Implement subscribe/unsubscribe logic for resource and resource template collections.
            // subscribe = true;
        }

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
                if (request.Params is not null &&
                    prompts.TryGetPrimitive(request.Params.Name, out var prompt))
                {
                    return prompt.GetAsync(request, cancellationToken);
                }

                return originalGetPromptHandler(request, cancellationToken);
            };

            listChanged = true;
        }

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
                if (request.Params is not null &&
                    tools.TryGetPrimitive(request.Params.Name, out var tool))
                {
                    return tool.InvokeAsync(request, cancellationToken);
                }

                return originalCallToolHandler(request, cancellationToken);
            };

            listChanged = true;
        }

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

        ServerCapabilities.Logging = new();
        ServerCapabilities.Logging.SetLoggingLevelHandler = setLoggingLevelHandler;

        RequestHandlers.Set(
            RequestMethods.LoggingSetLevel,
            (request, destinationTransport, cancellationToken) =>
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
                    return InvokeHandlerAsync(setLoggingLevelHandler, request, destinationTransport, cancellationToken);
                }

                // Otherwise, consider it handled.
                return new ValueTask<EmptyResult>(EmptyResult.Instance);
            },
            McpJsonUtilities.JsonContext.Default.SetLevelRequestParams,
            McpJsonUtilities.JsonContext.Default.EmptyResult);
    }

    private ValueTask<TResult> InvokeHandlerAsync<TParams, TResult>(
        Func<RequestContext<TParams>, CancellationToken, ValueTask<TResult>> handler,
        TParams? args,
        ITransport? destinationTransport = null,
        CancellationToken cancellationToken = default)
    {
        return _servicesScopePerRequest ?
            InvokeScopedAsync(handler, args, cancellationToken) :
            handler(new(new DestinationBoundMcpServer(this, destinationTransport)) { Params = args }, cancellationToken);

        async ValueTask<TResult> InvokeScopedAsync(
            Func<RequestContext<TParams>, CancellationToken, ValueTask<TResult>> handler,
            TParams? args,
            CancellationToken cancellationToken)
        {
            var scope = Services?.GetService<IServiceScopeFactory>()?.CreateAsyncScope();
            try
            {
                return await handler(
                    new RequestContext<TParams>(new DestinationBoundMcpServer(this, destinationTransport))
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

    private void SetHandler<TRequest, TResponse>(
        string method,
        Func<RequestContext<TRequest>, CancellationToken, ValueTask<TResponse>> handler,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo)
    {
        RequestHandlers.Set(method, 
            (request, destinationTransport, cancellationToken) =>
                InvokeHandlerAsync(handler, request, destinationTransport, cancellationToken),
            requestTypeInfo, responseTypeInfo);
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
}
