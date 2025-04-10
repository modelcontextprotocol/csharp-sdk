using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Shared;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.Runtime.CompilerServices;

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

    private readonly EventHandler? _toolsChangedDelegate;
    private readonly EventHandler? _promptsChangedDelegate;

    private string _endpointName;
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
        _endpointName = $"Server ({options.ServerInfo?.Name ?? DefaultImplementation.Name} {options.ServerInfo?.Version ?? DefaultImplementation.Version})";

        // Configure all request handlers based on the supplied options.
        SetInitializeHandler(options);
        SetToolsHandler(options);
        SetPromptsHandler(options);
        SetResourcesHandler(options);
        SetSetLoggingLevelHandler(options);
        SetCompletionHandler(options);
        SetPingHandler();

        // Register any notification handlers that were provided.
        if (options.Capabilities?.NotificationHandlers is { } notificationHandlers)
        {
            NotificationHandlers.RegisterRange(notificationHandlers);
        }

        // Now that everything has been configured, subscribe to any necessary notifications.
        if (ServerOptions.Capabilities?.Tools?.ToolCollection is { } tools)
        {
            _toolsChangedDelegate = delegate
            {
                _ = SendMessageAsync(new JsonRpcNotification() { Method = NotificationMethods.ToolListChangedNotification });
            };

            tools.Changed += _toolsChangedDelegate;
        }

        if (ServerOptions.Capabilities?.Prompts?.PromptCollection is { } prompts)
        {
            _promptsChangedDelegate = delegate
            {
                _ = SendMessageAsync(new JsonRpcNotification() { Method = NotificationMethods.PromptListChangedNotification });
            };

            prompts.Changed += _promptsChangedDelegate;
        }

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
        if (_toolsChangedDelegate is not null &&
            ServerOptions.Capabilities?.Tools?.ToolCollection is { } tools)
        {
            tools.Changed -= _toolsChangedDelegate;
        }

        if (_promptsChangedDelegate is not null &&
            ServerOptions.Capabilities?.Prompts?.PromptCollection is { } prompts)
        {
            prompts.Changed -= _promptsChangedDelegate;
        }

        await base.DisposeUnsynchronizedAsync().ConfigureAwait(false);
    }

    private void SetPingHandler()
    {
        RequestHandlers.Set(RequestMethods.Ping,
            (request, _) => Task.FromResult(new PingResult()),
            McpJsonUtilities.JsonContext.Default.JsonNode,
            McpJsonUtilities.JsonContext.Default.PingResult);
    }

    private void SetInitializeHandler(McpServerOptions options)
    {
        RequestHandlers.Set(RequestMethods.Initialize,
            (request, _) =>
            {
                ClientCapabilities = request?.Capabilities ?? new();
                ClientInfo = request?.ClientInfo;

                // Use the ClientInfo to update the session EndpointName for logging.
                _endpointName = $"{_endpointName}, Client ({ClientInfo?.Name} {ClientInfo?.Version})";
                GetSessionOrThrow().EndpointName = _endpointName;

                return Task.FromResult(new InitializeResult
                {
                    ProtocolVersion = options.ProtocolVersion,
                    Instructions = options.ServerInstructions,
                    ServerInfo = options.ServerInfo ?? DefaultImplementation,
                    Capabilities = ServerCapabilities ?? new(),
                });
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
            throw new McpException("Completions capability was enabled, but Complete handler was not specified.");

        // This capability is not optional, so return an empty result if there is no handler.
        RequestHandlers.Set(
            RequestMethods.CompletionComplete,
            (request, cancellationToken) => completeHandler(new(this, request), cancellationToken),
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

        if ((listResourcesHandler is not { } && listResourceTemplatesHandler is not { }) ||
            resourcesCapability.ReadResourceHandler is not { } readResourceHandler)
        {
            throw new McpException("Resources capability was enabled, but ListResources and/or ReadResource handlers were not specified.");
        }

        listResourcesHandler ??= (static (_, _) => Task.FromResult(new ListResourcesResult()));

        RequestHandlers.Set(
            RequestMethods.ResourcesList,
            (request, cancellationToken) => listResourcesHandler(new(this, request), cancellationToken),
            McpJsonUtilities.JsonContext.Default.ListResourcesRequestParams,
            McpJsonUtilities.JsonContext.Default.ListResourcesResult);

        RequestHandlers.Set(
            RequestMethods.ResourcesRead,
            (request, cancellationToken) => readResourceHandler(new(this, request), cancellationToken),
            McpJsonUtilities.JsonContext.Default.ReadResourceRequestParams,
            McpJsonUtilities.JsonContext.Default.ReadResourceResult);

        listResourceTemplatesHandler ??= (static (_, _) => Task.FromResult(new ListResourceTemplatesResult()));
        RequestHandlers.Set(
            RequestMethods.ResourcesTemplatesList,
            (request, cancellationToken) => listResourceTemplatesHandler(new(this, request), cancellationToken),
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
            throw new McpException("Resources capability was enabled with subscribe support, but SubscribeToResources and/or UnsubscribeFromResources handlers were not specified.");
        }

        RequestHandlers.Set(
            RequestMethods.ResourcesSubscribe,
            (request, cancellationToken) => subscribeHandler(new(this, request), cancellationToken),
            McpJsonUtilities.JsonContext.Default.SubscribeRequestParams,
            McpJsonUtilities.JsonContext.Default.EmptyResult);

        RequestHandlers.Set(
            RequestMethods.ResourcesUnsubscribe,
            (request, cancellationToken) => unsubscribeHandler(new(this, request), cancellationToken),
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
            throw new McpException("ListPrompts and GetPrompt handlers should be specified together.");
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

                    throw new McpException($"Unknown prompt '{request.Params?.Name}'");
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
                throw new McpException("ListPrompts and/or GetPrompt handlers were not specified but the Prompts capability was enabled.");
            }
        }

        RequestHandlers.Set(
            RequestMethods.PromptsList,
            (request, cancellationToken) => listPromptsHandler(new(this, request), cancellationToken),
            McpJsonUtilities.JsonContext.Default.ListPromptsRequestParams,
            McpJsonUtilities.JsonContext.Default.ListPromptsResult);

        RequestHandlers.Set(
            RequestMethods.PromptsGet,
            (request, cancellationToken) => getPromptHandler(new(this, request), cancellationToken),
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
            throw new McpException("ListTools and CallTool handlers should be specified together.");
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

                    throw new McpException($"Unknown tool '{request.Params?.Name}'");
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
                throw new McpException("ListTools and/or CallTool handlers were not specified but the Tools capability was enabled.");
            }
        }

        RequestHandlers.Set(
            RequestMethods.ToolsList,
            (request, cancellationToken) => listToolsHandler(new(this, request), cancellationToken),
            McpJsonUtilities.JsonContext.Default.ListToolsRequestParams,
            McpJsonUtilities.JsonContext.Default.ListToolsResult);

        RequestHandlers.Set(
            RequestMethods.ToolsCall,
            (request, cancellationToken) => callToolHandler(new(this, request), cancellationToken),
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
            (request, cancellationToken) =>
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
                    return setLoggingLevelHandler(new(this, request), cancellationToken);
                }

                // Otherwise, consider it handled.
                return EmptyResult.CompletedTask;
            },
            McpJsonUtilities.JsonContext.Default.SetLevelRequestParams,
            McpJsonUtilities.JsonContext.Default.EmptyResult);
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