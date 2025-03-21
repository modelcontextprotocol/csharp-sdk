using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Shared;
using ModelContextProtocol.Utils;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using System.Text.Json.Nodes;

namespace ModelContextProtocol.Server;

/// <inheritdoc />
internal sealed class McpServer : McpJsonRpcEndpoint, IMcpServer
{
    private readonly IServerTransport? _serverTransport;
    private readonly McpServerOptions _options;
    private volatile bool _isInitializing;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new instance of <see cref="McpServer"/>.
    /// </summary>
    /// <param name="transport">Transport to use for the server</param>
    /// <param name="options">Configuration options for this server, including capabilities. 
    /// Make sure to accurately reflect exactly what capabilities the server supports and does not support.</param>    
    /// <param name="loggerFactory">Logger factory to use for logging</param>
    /// <param name="serviceProvider">Optional service provider to use for dependency injection</param>
    /// <exception cref="McpServerException"></exception>
    public McpServer(ITransport transport, McpServerOptions options, ILoggerFactory? loggerFactory, IServiceProvider? serviceProvider)
        : base(transport, loggerFactory)
    {
        Throw.IfNull(options);

        _serverTransport = transport as IServerTransport;
        _options = options;
        _logger = (ILogger?)loggerFactory?.CreateLogger<McpServer>() ?? NullLogger.Instance;
        ServerInstructions = options.ServerInstructions;
        ServiceProvider = serviceProvider;

        AddNotificationHandler("notifications/initialized", _ =>
        {
            IsInitialized = true;
            return Task.CompletedTask;
        });

        SetInitializeHandler(options);
        SetCompletionHandler(options);
        SetPingHandler();
        SetToolsHandler(options);
        SetPromptsHandler(options);
        SetResourcesHandler(options);
        SetSetLoggingLevelHandler(options);
    }

    public ClientCapabilities? ClientCapabilities { get; set; }

    /// <inheritdoc />
    public Implementation? ClientInfo { get; set; }

    /// <inheritdoc />
    public string? ServerInstructions { get; set; }

    /// <inheritdoc />
    public IServiceProvider? ServiceProvider { get; }

    /// <inheritdoc />
    public override string EndpointName =>
        $"Server ({_options.ServerInfo.Name} {_options.ServerInfo.Version}), Client ({ClientInfo?.Name} {ClientInfo?.Version})";

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitializing)
        {
            _logger.ServerAlreadyInitializing(EndpointName);
            throw new InvalidOperationException("Server is already initializing");
        }
        _isInitializing = true;

        if (IsInitialized)
        {
            _logger.ServerAlreadyInitializing(EndpointName);
            return;
        }

        try
        {
            CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            if (_serverTransport is not null)
            {
                // Start listening for messages
                await _serverTransport.StartListeningAsync(CancellationTokenSource.Token).ConfigureAwait(false);
            }

            // Start processing messages
            MessageProcessingTask = ProcessMessagesAsync(CancellationTokenSource.Token);

            // Unlike McpClient, we're not done initializing until we've received a message from the client, so we don't set IsInitialized here
        }
        catch (Exception e)
        {
            _logger.ServerInitializationError(EndpointName, e);
            await CleanupAsync().ConfigureAwait(false);
            throw;
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
                return Task.FromResult(new InitializeResult()
                {
                    ProtocolVersion = options.ProtocolVersion,
                    Instructions = ServerInstructions,
                    ServerInfo = _options.ServerInfo,
                    Capabilities = options.Capabilities ?? new ServerCapabilities(),
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

        if (resourcesCapability.ListResourcesHandler is not { } listResourcesHandler ||
            resourcesCapability.ReadResourceHandler is not { } readResourceHandler)
        {
            throw new McpServerException("Resources capability was enabled, but ListResources and/or ReadResource handlers were not specified.");
        }

        SetRequestHandler<ListResourcesRequestParams, ListResourcesResult>("resources/list", (request, ct) => listResourcesHandler(new(this, request), ct));
        SetRequestHandler<ReadResourceRequestParams, ReadResourceResult>("resources/read", (request, ct) => readResourceHandler(new(this, request), ct));

        // Set the list resource templates handler, or use the default if not specified
        var listResourceTemplatesHandler = resourcesCapability.ListResourceTemplatesHandler
            ?? (static (_, _) => Task.FromResult(new ListResourceTemplatesResult()));

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
        if (options.Capabilities?.Tools is not { } toolsCapability)
        {
            return;
        }

        if (toolsCapability.ListToolsHandler is not { } listToolsHandler ||
            toolsCapability.CallToolHandler is not { } callToolHandler)
        {
            throw new McpServerException("ListTools and/or CallTool handlers were specified but the Tools capability was not enabled.");
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
