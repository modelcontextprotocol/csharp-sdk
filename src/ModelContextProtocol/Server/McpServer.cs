using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Shared;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
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

    private string _endpointName;
    private int _started;

    /// <summary>Holds a boxed <see cref="LoggingLevel"/> value for the server.</summary>
    /// <remarks>
    /// Initialized to non-null the first time SetLevel is used. This is stored as a strong box
    /// rather than a nullable to be able to manipulate it atomically.
    /// </remarks>
    private StrongBox<LoggingLevel>? _loggingLevel;
    private readonly List<Action> _disposables = [];

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
        _endpointName = $"Server ({options.ServerInfo?.Name ?? DefaultImplementation.Name} {options.ServerInfo?.Version ?? DefaultImplementation.Version})";
        _servicesScopePerRequest = options.ScopeRequests;

        // Configure all request handlers based on the supplied options.
        SetInitializeHandler(options);
        SetToolsHandler(options);
        SetPromptsHandler(options);
        SetResourcesHandler(options);
        SetSetLoggingLevelHandler(options);
        SetCompletionHandler(options);
        SetPingHandler();

        var capabilities = options.Capabilities;

        // Register any notification handlers that were provided.
        if (capabilities?.NotificationHandlers is { } notificationHandlers)
        {
            NotificationHandlers.RegisterRange(notificationHandlers);
        }
        
        RegisterListChange(capabilities?.Tools, NotificationMethods.ToolListChangedNotification);
        RegisterListChange(capabilities?.Prompts, NotificationMethods.PromptListChangedNotification);
        RegisterListChange(capabilities?.Resources, NotificationMethods.ResourceListChangedNotification);

        // And initialize the session.
        InitializeSession(transport);
    }

    public ServerCapabilities? ServerCapabilities { get; set; }

    /// <inheritdoc />
    public ClientCapabilities? ClientCapabilities { get; set; }

    /// <inheritdoc />
    public Implementation? ClientInfo { get; set; }

    /// <inheritdoc />
    public McpServerOptions ServerOptions { get; }

    /// <inheritdoc />
    public IServiceProvider? Services { get; }

    /// <inheritdoc />
    public override string EndpointName => _endpointName;

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
        foreach (var disposable in _disposables)
        {
            disposable();
        }
        _disposables.Clear();
        await base.DisposeUnsynchronizedAsync().ConfigureAwait(false);
    }

    private void SetPingHandler()
    {
        SetHandler(RequestMethods.Ping,
            async (request, _) => new PingResult(),
            McpJsonUtilities.JsonContext.Default.JsonNode,
            McpJsonUtilities.JsonContext.Default.PingResult);
    }

    private void SetInitializeHandler(McpServerOptions options)
    {
        RequestHandlers.Set(RequestMethods.Initialize,
            async (request, _, _) =>
            {
                ClientCapabilities = request?.Capabilities ?? new();
                ClientInfo = request?.ClientInfo;

                // Use the ClientInfo to update the session EndpointName for logging.
                _endpointName = $"{_endpointName}, Client ({ClientInfo?.Name} {ClientInfo?.Version})";
                GetSessionOrThrow().EndpointName = _endpointName;

                return new InitializeResult
                {
                    ProtocolVersion = options.ProtocolVersion,
                    Instructions = options.ServerInstructions,
                    ServerInfo = options.ServerInfo ?? DefaultImplementation,
                    Capabilities = ServerCapabilities ?? new(),
                };
            },
            McpJsonUtilities.JsonContext.Default.InitializeRequestParams,
            McpJsonUtilities.JsonContext.Default.InitializeResult);
    }

    private void SetCompletionHandler(McpServerOptions options)
    {
        if (options.Capabilities?.Completions is not { } completionsCapability)
        {
            return;
        }

        var completeHandler = completionsCapability.CompleteHandler ??
            throw new InvalidOperationException(
                $"{nameof(ServerCapabilities)}.{nameof(ServerCapabilities.Completions)} was enabled, " +
                $"but {nameof(CompletionsCapability.CompleteHandler)} was not specified.");

        // This capability is not optional, so return an empty result if there is no handler.
        SetHandler(
            RequestMethods.CompletionComplete,
            completeHandler,
            McpJsonUtilities.JsonContext.Default.CompleteRequestParams,
            McpJsonUtilities.JsonContext.Default.CompleteResult);
    }

    private void SetResourcesHandler(McpServerOptions options)
    {
        if (options.Capabilities?.Resources is not { } resourcesCapability)
        {
            return;
        }

        var listResourcesHandler = resourcesCapability.ListResourcesHandler;
        var listResourceTemplatesHandler = resourcesCapability.ListResourceTemplatesHandler;
        var readResourceHandler = resourcesCapability.ReadResourceHandler;
        var resourceCollection = resourcesCapability.ResourceCollection;

        var originalListResourcesHandler = listResourcesHandler;
        listResourcesHandler = async (request, cancellationToken) =>
        {
            ListResourcesResult result = originalListResourcesHandler is not null ?
                await originalListResourcesHandler(request, cancellationToken).ConfigureAwait(false) :
                new();

            if (request.Params?.Cursor is null && resourceCollection is not null)
            {
                result.Resources.AddRange(resourceCollection.Select(t => t.ProtocolResource));
            }

            return result;
        };

        var isMissingListResourceHandlers = originalListResourcesHandler is null && listResourceTemplatesHandler is null;
        if (resourceCollection is not { IsEmpty: false } && (isMissingListResourceHandlers || readResourceHandler is not { }))
        {
            throw new InvalidOperationException(
                $"{nameof(ServerCapabilities)}.{nameof(ServerCapabilities.Resources)} was enabled, " +
                $"but {nameof(ResourcesCapability.ListResourcesHandler)} or {nameof(ResourcesCapability.ReadResourceHandler)} was not specified.");
        }

        listResourcesHandler ??= static async (_, _) => new ListResourcesResult();

        SetHandler(
            RequestMethods.ResourcesList,
            listResourcesHandler,
            McpJsonUtilities.JsonContext.Default.ListResourcesRequestParams,
            McpJsonUtilities.JsonContext.Default.ListResourcesResult);

        readResourceHandler ??= static async (_, _) => new();
        SetHandler(
            RequestMethods.ResourcesRead,
            readResourceHandler,
            McpJsonUtilities.JsonContext.Default.ReadResourceRequestParams,
            McpJsonUtilities.JsonContext.Default.ReadResourceResult);

        listResourceTemplatesHandler ??= static async (_, _) => new ListResourceTemplatesResult();
        SetHandler(
            RequestMethods.ResourcesTemplatesList,
            listResourceTemplatesHandler,
            McpJsonUtilities.JsonContext.Default.ListResourceTemplatesRequestParams,
            McpJsonUtilities.JsonContext.Default.ListResourceTemplatesResult);

        if (resourcesCapability.Subscribe is not true)
        {
            return;
        }

        var subscribeHandler = resourcesCapability.SubscribeToResourcesHandler;
        var unsubscribeHandler = resourcesCapability.UnsubscribeFromResourcesHandler;
        if (subscribeHandler is null || unsubscribeHandler is null)
        {
            throw new InvalidOperationException(
                $"{nameof(ServerCapabilities)}.{nameof(ServerCapabilities.Resources)}.{nameof(ResourcesCapability.Subscribe)} is set, " +
                $"but {nameof(ResourcesCapability.SubscribeToResourcesHandler)} or {nameof(ResourcesCapability.UnsubscribeFromResourcesHandler)} was not specified.");
        }

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

    private void SetPromptsHandler(McpServerOptions options)
    {
        PromptsCapability? promptsCapability = options.Capabilities?.Prompts;
        var listPromptsHandler = promptsCapability?.ListPromptsHandler;
        var getPromptHandler = promptsCapability?.GetPromptHandler;
        var prompts = promptsCapability?.PromptCollection;

        if (listPromptsHandler is null != getPromptHandler is null)
        {
            throw new InvalidOperationException(
                $"{nameof(PromptsCapability)}.{nameof(promptsCapability.ListPromptsHandler)} or " +
                $"{nameof(PromptsCapability)}.{nameof(promptsCapability.GetPromptHandler)} was specified without the other. " +
                $"Both or neither must be provided.");
        }

        // Handle prompts provided via DI.
        if (prompts is { IsEmpty: false })
        {
            // Synthesize the handlers, making sure a PromptsCapability is specified.
            var originalListPromptsHandler = listPromptsHandler;
            listPromptsHandler = async (request, cancellationToken) =>
            {
                ListPromptsResult result = originalListPromptsHandler is not null ?
                    await originalListPromptsHandler(request, cancellationToken).ConfigureAwait(false) :
                    new();

                if (request.Params?.Cursor is null)
                {
                    result.Prompts.AddRange(prompts.Select(t => t.ProtocolPrompt));
                }

                return result;
            };

            var originalGetPromptHandler = getPromptHandler;
            getPromptHandler = (request, cancellationToken) =>
            {
                if (request.Params is null ||
                    !prompts.TryGetPrimitive(request.Params.Name, out var prompt))
                {
                    if (originalGetPromptHandler is not null)
                    {
                        return originalGetPromptHandler(request, cancellationToken);
                    }

                    throw new McpException($"Unknown prompt: '{request.Params?.Name}'", McpErrorCode.InvalidParams);
                }

                return prompt.GetAsync(request, cancellationToken);
            };

            ServerCapabilities = new()
            {
                Experimental = options.Capabilities?.Experimental,
                Logging = options.Capabilities?.Logging,
                Tools = options.Capabilities?.Tools,
                Resources = options.Capabilities?.Resources,
                Prompts = new()
                {
                    ListPromptsHandler = listPromptsHandler,
                    GetPromptHandler = getPromptHandler,
                    PromptCollection = prompts,
                    ListChanged = true,
                }
            };
        }
        else
        {
            ServerCapabilities = options.Capabilities;

            if (promptsCapability is null)
            {
                // No prompts, and no prompts capability was declared, so nothing to do.
                return;
            }

            // Make sure the handlers are provided if the capability is enabled.
            if (listPromptsHandler is null || getPromptHandler is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(ServerCapabilities)}.{nameof(ServerCapabilities.Prompts)} was enabled, " +
                    $"but {nameof(PromptsCapability.ListPromptsHandler)} or {nameof(PromptsCapability.GetPromptHandler)} was not specified.");
            }
        }

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

    private void SetToolsHandler(McpServerOptions options)
    {
        ToolsCapability? toolsCapability = options.Capabilities?.Tools;
        var listToolsHandler = toolsCapability?.ListToolsHandler;
        var callToolHandler = toolsCapability?.CallToolHandler;
        var tools = toolsCapability?.ToolCollection;

        if (listToolsHandler is null != callToolHandler is null)
        {
            throw new InvalidOperationException(
                $"{nameof(ToolsCapability)}.{nameof(ToolsCapability.ListToolsHandler)} or " +
                $"{nameof(ToolsCapability)}.{nameof(ToolsCapability.CallToolHandler)} was specified without the other. " +
                $"Both or neither must be provided.");
        }

        // Handle tools provided via DI.
        if (tools is { IsEmpty: false })
        {
            // Synthesize the handlers, making sure a ToolsCapability is specified.
            var originalListToolsHandler = listToolsHandler;
            listToolsHandler = async (request, cancellationToken) =>
            {
                ListToolsResult result = originalListToolsHandler is not null ?
                    await originalListToolsHandler(request, cancellationToken).ConfigureAwait(false) :
                    new();

                if (request.Params?.Cursor is null)
                {
                    result.Tools.AddRange(tools.Select(t => t.ProtocolTool));
                }

                return result;
            };

            var originalCallToolHandler = callToolHandler;
            callToolHandler = (request, cancellationToken) =>
            {
                if (request.Params is null ||
                    !tools.TryGetPrimitive(request.Params.Name, out var tool))
                {
                    if (originalCallToolHandler is not null)
                    {
                        return originalCallToolHandler(request, cancellationToken);
                    }

                    throw new McpException($"Unknown tool: '{request.Params?.Name}'", McpErrorCode.InvalidParams);
                }

                return tool.InvokeAsync(request, cancellationToken);
            };

            ServerCapabilities = new()
            {
                Experimental = options.Capabilities?.Experimental,
                Logging = options.Capabilities?.Logging,
                Prompts = options.Capabilities?.Prompts,
                Resources = options.Capabilities?.Resources,
                Tools = new()
                {
                    ListToolsHandler = listToolsHandler,
                    CallToolHandler = callToolHandler,
                    ToolCollection = tools,
                    ListChanged = true,
                }
            };
        }
        else
        {
            ServerCapabilities = options.Capabilities;

            if (toolsCapability is null)
            {
                // No tools, and no tools capability was declared, so nothing to do.
                return;
            }

            // Make sure the handlers are provided if the capability is enabled.
            if (listToolsHandler is null || callToolHandler is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(ServerCapabilities)}.{nameof(ServerCapabilities.Tools)} was enabled, " +
                    $"but {nameof(ToolsCapability.ListToolsHandler)} or {nameof(ToolsCapability.CallToolHandler)} was not specified.");
            }
        }

        SetHandler(
            RequestMethods.ToolsList,
            listToolsHandler,
            McpJsonUtilities.JsonContext.Default.ListToolsRequestParams,
            McpJsonUtilities.JsonContext.Default.ListToolsResult);

        SetHandler(
            RequestMethods.ToolsCall,
            callToolHandler,
            McpJsonUtilities.JsonContext.Default.CallToolRequestParams,
            McpJsonUtilities.JsonContext.Default.CallToolResponse);
    }

    private void SetSetLoggingLevelHandler(McpServerOptions options)
    {
        // We don't require that the handler be provided, as we always store the provided
        // log level to the server.
        var setLoggingLevelHandler = options.Capabilities?.Logging?.SetLoggingLevelHandler;

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

    private void RegisterListChange<T>(IListCapability<T>? capability, string methodName)
        where T : IMcpServerPrimitive
    {
        // https://modelcontextprotocol.io/specification/2024-11-05/server/tools#capabilities
        // Look to spec for guidance on ListChanged over collection existance.
        if (capability?.Collection is { } collection)
            //&& capability.ListChanged is true)
        {
            void ChangedDelegate(object? sender, EventArgs e)
                => _ = this.SendNotificationAsync(methodName);
            collection.Changed += ChangedDelegate;
            _disposables.Add(() => collection.Changed -= ChangedDelegate);
        }
    }

    /// <summary>Maps a <see cref="LogLevel"/> to a <see cref="LoggingLevel"/>.</summary>
    internal static LoggingLevel ToLoggingLevel(LogLevel level) =>
        level switch
        {
            LogLevel.Trace => Protocol.Types.LoggingLevel.Debug,
            LogLevel.Debug => Protocol.Types.LoggingLevel.Debug,
            LogLevel.Information => Protocol.Types.LoggingLevel.Info,
            LogLevel.Warning => Protocol.Types.LoggingLevel.Warning,
            LogLevel.Error => Protocol.Types.LoggingLevel.Error,
            LogLevel.Critical => Protocol.Types.LoggingLevel.Critical,
            _ => Protocol.Types.LoggingLevel.Emergency,
        };
}
