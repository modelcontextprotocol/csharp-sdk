using Microsoft.Extensions.Logging;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Shared;
using ModelContextProtocol.Utils;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Server;

/// <inheritdoc />
internal sealed class McpServer : McpJsonRpcEndpoint, IMcpServer
{
    private readonly IServerTransport? _serverTransport;
    private readonly EventHandler? _toolsChangedDelegate;

    private ITransport? _sessionTransport;
    private string _endpointName;

    /// <summary>
    /// Creates a new instance of <see cref="McpServer"/>.
    /// </summary>
    /// <param name="serverTransport">Transport to use for the server that is ready to accept new sessions asynchronously.</param>
    /// <param name="options">Configuration options for this server, including capabilities.
    /// Make sure to accurately reflect exactly what capabilities the server supports and does not support.</param>
    /// <param name="loggerFactory">Logger factory to use for logging</param>
    /// <param name="serviceProvider">Optional service provider to use for dependency injection</param>
    /// <exception cref="McpServerException"></exception>
    public McpServer(IServerTransport serverTransport, McpServerOptions options, ILoggerFactory? loggerFactory, IServiceProvider? serviceProvider)
        : this(options, loggerFactory, serviceProvider)
    {
        Throw.IfNull(serverTransport);

        _serverTransport = serverTransport;
    }

    /// <summary>
    /// Creates a new instance of <see cref="McpServer"/>.
    /// </summary>
    /// <param name="transport">Transport to use for the server representing an already-established session.</param>
    /// <param name="options">Configuration options for this server, including capabilities.
    /// Make sure to accurately reflect exactly what capabilities the server supports and does not support.</param>
    /// <param name="loggerFactory">Logger factory to use for logging</param>
    /// <param name="serviceProvider">Optional service provider to use for dependency injection</param>
    /// <exception cref="McpServerException"></exception>
    public McpServer(ITransport transport, McpServerOptions options, ILoggerFactory? loggerFactory, IServiceProvider? serviceProvider)
        : this(options, loggerFactory, serviceProvider)
    {
        Throw.IfNull(transport);

        _sessionTransport = transport;
        InitializeSession(transport);
    }

    /// <summary>
    /// Creates a new instance of <see cref="McpServer"/>.
    /// </summary>
    /// <param name="options">Configuration options for this server, including capabilities. 
    /// Make sure to accurately reflect exactly what capabilities the server supports and does not support.</param>
    /// <param name="loggerFactory">Logger factory to use for logging</param>
    /// <param name="serviceProvider">Optional service provider to use for dependency injection</param>
    /// <exception cref="McpServerException"></exception>
    private McpServer(McpServerOptions options, ILoggerFactory? loggerFactory, IServiceProvider? serviceProvider)
        : base(loggerFactory)
    {
        Throw.IfNull(options);

        ServerOptions = options;
        Services = serviceProvider;
        _endpointName = $"Server ({options.ServerInfo.Name} {options.ServerInfo.Version})";

        _toolsChangedDelegate = delegate
        {
            _ = SendMessageAsync(new JsonRpcNotification()
            {
                Method = NotificationMethods.ToolListChangedNotification,
            });
        };

        AddNotificationHandler("notifications/initialized", _ =>
        {
            if (ServerOptions.Capabilities?.Tools?.ToolCollection is { } tools)
            {
                tools.Changed += _toolsChangedDelegate;
            }

            return Task.CompletedTask;
        });

        SetToolsHandler(options);

        SetInitializeHandler(options);
        SetCompletionHandler(options);
        SetPingHandler();
        SetPromptsHandler(options);
        SetResourcesHandler(options);
        SetSetLoggingLevelHandler(options);
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

    public async Task AcceptSessionAsync(CancellationToken cancellationToken = default)
    {
        // Below is effectively an assertion. The McpServerFactory should only use this with the IServerTransport constructor.
        Throw.IfNull(_serverTransport);

        try
        {
            _sessionTransport = await _serverTransport.AcceptAsync(cancellationToken).ConfigureAwait(false);

            if (_sessionTransport is null)
            {
                throw new McpServerException("The server transport closed before a client started a new session.");
            }

            InitializeSession(_sessionTransport);
        }
        catch (Exception e)
        {
            _logger.ServerInitializationError(EndpointName, e);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Start processing messages
            StartSession(fullSessionCancellationToken: cancellationToken);
            await MessageProcessingTask.ConfigureAwait(false);
        }
        finally
        {
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    public override async ValueTask DisposeUnsynchronizedAsync()
    {
        if (ServerOptions.Capabilities?.Tools?.ToolCollection is { } tools)
        {
            tools.Changed -= _toolsChangedDelegate;
        }

        try
        {
            await base.DisposeUnsynchronizedAsync().ConfigureAwait(false);
        }
        finally
        {
            if (_serverTransport is not null && _sessionTransport is not null)
            {
                // We created the _sessionTransport from the _serverTransport, so we own it.
                await _sessionTransport.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private void SetPingHandler()
    {
        SetRequestHandler<JsonNode, PingResult>("ping",
            (request, _) => Task.FromResult(new PingResult()));
    }

    private void SetInitializeHandler(McpServerOptions options)
    {
        SetRequestHandler<InitializeRequestParams, InitializeResult>("initialize",
            (request, _) =>
            {
                ClientCapabilities = request?.Capabilities ?? new();
                ClientInfo = request?.ClientInfo;

                // Use the ClientInfo to update the session EndpointName for logging.
                _endpointName = $"{_endpointName}, Client ({ClientInfo?.Name} {ClientInfo?.Version})";
                GetSessionOrThrow().EndpointName = EndpointName;

                return Task.FromResult(new InitializeResult()
                {
                    ProtocolVersion = options.ProtocolVersion,
                    Instructions = options.ServerInstructions,
                    ServerInfo = options.ServerInfo,
                    Capabilities = ServerCapabilities ?? new(),
                });
            });
    }

    private void SetCompletionHandler(McpServerOptions options)
    {
        // This capability is not optional, so return an empty result if there is no handler.
        SetRequestHandler<CompleteRequestParams, CompleteResult>("completion/complete",
            options.GetCompletionHandler is { } handler ?
                (request, ct) => handler(new(this, request), ct) :
                (request, ct) => Task.FromResult(new CompleteResult() { Completion = new() { Values = [], Total = 0, HasMore = false } }));
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
            throw new McpServerException("Resources capability was enabled, but ListResources and/or ReadResource handlers were not specified.");
        }

        listResourcesHandler ??= (static (_, _) => Task.FromResult(new ListResourcesResult()));

        SetRequestHandler<ListResourcesRequestParams, ListResourcesResult>("resources/list", (request, ct) => listResourcesHandler(new(this, request), ct));
        SetRequestHandler<ReadResourceRequestParams, ReadResourceResult>("resources/read", (request, ct) => readResourceHandler(new(this, request), ct));

        listResourceTemplatesHandler ??= (static (_, _) => Task.FromResult(new ListResourceTemplatesResult()));
        SetRequestHandler<ListResourceTemplatesRequestParams, ListResourceTemplatesResult>("resources/templates/list", (request, ct) => listResourceTemplatesHandler(new(this, request), ct));

        if (resourcesCapability.Subscribe is not true)
        {
            return;
        }

        var subscribeHandler = resourcesCapability.SubscribeToResourcesHandler;
        var unsubscribeHandler = resourcesCapability.UnsubscribeFromResourcesHandler;
        if (subscribeHandler is null || unsubscribeHandler is null)
        {
            throw new McpServerException("Resources capability was enabled with subscribe support, but SubscribeToResources and/or UnsubscribeFromResources handlers were not specified.");
        }

        SetRequestHandler<SubscribeRequestParams, EmptyResult>("resources/subscribe", (request, ct) => subscribeHandler(new(this, request), ct));
        SetRequestHandler<UnsubscribeRequestParams, EmptyResult>("resources/unsubscribe", (request, ct) => unsubscribeHandler(new(this, request), ct));
    }

    private void SetPromptsHandler(McpServerOptions options)
    {
        if (options.Capabilities?.Prompts is not { } promptsCapability)
        {
            return;
        }

        if (promptsCapability.ListPromptsHandler is not { } listPromptsHandler ||
            promptsCapability.GetPromptHandler is not { } getPromptHandler)
        {
            throw new McpServerException("Prompts capability was enabled, but ListPrompts and/or GetPrompt handlers were not specified.");
        }

        SetRequestHandler<ListPromptsRequestParams, ListPromptsResult>("prompts/list", (request, ct) => listPromptsHandler(new(this, request), ct));
        SetRequestHandler<GetPromptRequestParams, GetPromptResult>("prompts/get", (request, ct) => getPromptHandler(new(this, request), ct));
    }

    private void SetToolsHandler(McpServerOptions options)
    {
        ToolsCapability? toolsCapability = options.Capabilities?.Tools;
        var listToolsHandler = toolsCapability?.ListToolsHandler;
        var callToolHandler = toolsCapability?.CallToolHandler;
        var tools = toolsCapability?.ToolCollection;

        if (listToolsHandler is null != callToolHandler is null)
        {
            throw new McpServerException("ListTools and CallTool handlers should be specified together.");
        }

        // Handle tools provided via DI.
        if (tools is { IsEmpty: false })
        {
            var originalListToolsHandler = listToolsHandler;
            var originalCallToolHandler = callToolHandler;

            // Synthesize the handlers, making sure a ToolsCapability is specified.
            listToolsHandler = async (request, cancellationToken) =>
            {
                ListToolsResult result = new();
                foreach (McpServerTool tool in tools)
                {
                    result.Tools.Add(tool.ProtocolTool);
                }

                if (originalListToolsHandler is not null)
                {
                    string? nextCursor = null;
                    do
                    {
                        ListToolsResult extraResults = await originalListToolsHandler(request, cancellationToken).ConfigureAwait(false);
                        result.Tools.AddRange(extraResults.Tools);

                        nextCursor = extraResults.NextCursor;
                        if (nextCursor is not null)
                        {
                            request = request with { Params = new() { Cursor = nextCursor } };
                        }
                    }
                    while (nextCursor is not null);
                }

                return result;
            };

            callToolHandler = (request, cancellationToken) =>
            {
                if (request.Params is null ||
                    !tools.TryGetTool(request.Params.Name, out var tool))
                {
                    if (originalCallToolHandler is not null)
                    {
                        return originalCallToolHandler(request, cancellationToken);
                    }

                    throw new McpServerException($"Unknown tool '{request.Params?.Name}'");
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
                throw new McpServerException("ListTools and/or CallTool handlers were not specified but the Tools capability was enabled.");
            }
        }

        SetRequestHandler<ListToolsRequestParams, ListToolsResult>("tools/list", (request, ct) => listToolsHandler(new(this, request), ct));
        SetRequestHandler<CallToolRequestParams, CallToolResponse>("tools/call", (request, ct) => callToolHandler(new(this, request), ct));
    }

    private void SetSetLoggingLevelHandler(McpServerOptions options)
    {
        if (options.Capabilities?.Logging is not { } loggingCapability)
        {
            return;
        }

        if (loggingCapability.SetLoggingLevelHandler is not { } setLoggingLevelHandler)
        {
            throw new McpServerException("Logging capability was enabled, but SetLoggingLevelHandler was not specified.");
        }

        SetRequestHandler<SetLevelRequestParams, EmptyResult>("logging/setLevel", (request, ct) => setLoggingLevelHandler(new(this, request), ct));
    }
}