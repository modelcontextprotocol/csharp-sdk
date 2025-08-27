using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Client;

/// <inheritdoc/>
internal sealed partial class McpClientImpl : McpClient
{
    private static Implementation DefaultImplementation { get; } = new()
    {
        Name = AssemblyNameHelper.DefaultAssemblyName.Name ?? nameof(McpClient),
        Version = AssemblyNameHelper.DefaultAssemblyName.Version?.ToString() ?? "1.0.0",
    };

    private readonly ILogger _logger;
    private readonly ITransport _transport;
    private readonly string _endpointName;
    private readonly McpClientOptions _options;
    private readonly McpSessionHandler _sessionHandler;
    private readonly SemaphoreSlim _disposeLock = new(1, 1);

    private CancellationTokenSource? _connectCts;

    private ServerCapabilities? _serverCapabilities;
    private Implementation? _serverInfo;
    private string? _serverInstructions;
    private string? _negotiatedProtocolVersion;

    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpClientImpl"/> class.
    /// </summary>
    /// <param name="transport">The transport to use for communication with the server.</param>
    /// <param name="endpointName">The name of the endpoint for logging and debug purposes.</param>
    /// <param name="options">Options for the client, defining protocol version and capabilities.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    internal McpClientImpl(ITransport transport, string endpointName, McpClientOptions? options, ILoggerFactory? loggerFactory)
    {
        options ??= new();

        _transport = transport;
        _endpointName = $"Client ({options.ClientInfo?.Name ?? DefaultImplementation.Name} {options.ClientInfo?.Version ?? DefaultImplementation.Version})";
        _options = options;
        _logger = loggerFactory?.CreateLogger<McpClient>() ?? NullLogger<McpClient>.Instance;

        var notificationHandlers = new NotificationHandlers();
        var requestHandlers = new RequestHandlers();

        if (options.Capabilities is { } capabilities)
        {
            RegisterHandlers(capabilities, notificationHandlers, requestHandlers);
        }

        _sessionHandler = new McpSessionHandler(isServer: false, transport, endpointName, requestHandlers, notificationHandlers, _logger);
    }

    private void RegisterHandlers(ClientCapabilities capabilities, NotificationHandlers notificationHandlers, RequestHandlers requestHandlers)
    {
        if (capabilities.NotificationHandlers is { } notificationHandlersFromCapabilities)
        {
            notificationHandlers.RegisterRange(notificationHandlersFromCapabilities);
        }

        if (capabilities.Sampling is { } samplingCapability)
        {
            if (samplingCapability.SamplingHandler is not { } samplingHandler)
            {
                throw new InvalidOperationException("Sampling capability was set but it did not provide a handler.");
            }

            requestHandlers.Set(
                RequestMethods.SamplingCreateMessage,
                (request, _, cancellationToken) => samplingHandler(
                    request,
                    request?.ProgressToken is { } token ? new TokenProgress(this, token) : NullProgress.Instance,
                    cancellationToken),
                McpJsonUtilities.JsonContext.Default.CreateMessageRequestParams,
                McpJsonUtilities.JsonContext.Default.CreateMessageResult);
        }

        if (capabilities.Roots is { } rootsCapability)
        {
            if (rootsCapability.RootsHandler is not { } rootsHandler)
            {
                throw new InvalidOperationException("Roots capability was set but it did not provide a handler.");
            }

            requestHandlers.Set(
                RequestMethods.RootsList,
                (request, _, cancellationToken) => rootsHandler(request, cancellationToken),
                McpJsonUtilities.JsonContext.Default.ListRootsRequestParams,
                McpJsonUtilities.JsonContext.Default.ListRootsResult);
        }

        if (capabilities.Elicitation is { } elicitationCapability)
        {
            if (elicitationCapability.ElicitationHandler is not { } elicitationHandler)
            {
                throw new InvalidOperationException("Elicitation capability was set but it did not provide a handler.");
            }

            requestHandlers.Set(
                RequestMethods.ElicitationCreate,
                (request, _, cancellationToken) => elicitationHandler(request, cancellationToken),
                McpJsonUtilities.JsonContext.Default.ElicitRequestParams,
                McpJsonUtilities.JsonContext.Default.ElicitResult);
        }
    }

    /// <inheritdoc/>
    public override string? SessionId => _transport.SessionId;

    /// <inheritdoc/>
    public override string? NegotiatedProtocolVersion => _negotiatedProtocolVersion;

    /// <inheritdoc/>
    public override ServerCapabilities ServerCapabilities => _serverCapabilities ?? throw new InvalidOperationException("The client is not connected.");

    /// <inheritdoc/>
    public override Implementation ServerInfo => _serverInfo ?? throw new InvalidOperationException("The client is not connected.");

    /// <inheritdoc/>
    public override string? ServerInstructions => _serverInstructions;

    /// <summary>
    /// Asynchronously connects to an MCP server, establishes the transport connection, and completes the initialization handshake.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationToken = _connectCts.Token;

        try
        {
            // We don't want the ConnectAsync token to cancel the message processing loop after we've successfully connected.
            // The session handler handles cancelling the loop upon its disposal.
            _ = _sessionHandler.ProcessMessagesAsync(CancellationToken.None);

            // Perform initialization sequence
            using var initializationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            initializationCts.CancelAfter(_options.InitializationTimeout);

            try
            {
                // Send initialize request
                string requestProtocol = _options.ProtocolVersion ?? McpSessionHandler.LatestProtocolVersion;
                var initializeResponse = await this.SendRequestAsync(
                    RequestMethods.Initialize,
                    new InitializeRequestParams
                    {
                        ProtocolVersion = requestProtocol,
                        Capabilities = _options.Capabilities ?? new ClientCapabilities(),
                        ClientInfo = _options.ClientInfo ?? DefaultImplementation,
                    },
                    McpJsonUtilities.JsonContext.Default.InitializeRequestParams,
                    McpJsonUtilities.JsonContext.Default.InitializeResult,
                    cancellationToken: initializationCts.Token).ConfigureAwait(false);

                // Store server information
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    LogServerCapabilitiesReceived(_endpointName,
                        capabilities: JsonSerializer.Serialize(initializeResponse.Capabilities, McpJsonUtilities.JsonContext.Default.ServerCapabilities),
                        serverInfo: JsonSerializer.Serialize(initializeResponse.ServerInfo, McpJsonUtilities.JsonContext.Default.Implementation));
                }

                _serverCapabilities = initializeResponse.Capabilities;
                _serverInfo = initializeResponse.ServerInfo;
                _serverInstructions = initializeResponse.Instructions;

                // Validate protocol version
                bool isResponseProtocolValid =
                    _options.ProtocolVersion is { } optionsProtocol ? optionsProtocol == initializeResponse.ProtocolVersion :
                    McpSessionHandler.SupportedProtocolVersions.Contains(initializeResponse.ProtocolVersion);
                if (!isResponseProtocolValid)
                {
                    LogServerProtocolVersionMismatch(_endpointName, requestProtocol, initializeResponse.ProtocolVersion);
                    throw new McpException($"Server protocol version mismatch. Expected {requestProtocol}, got {initializeResponse.ProtocolVersion}");
                }

                _negotiatedProtocolVersion = initializeResponse.ProtocolVersion;

                // Send initialized notification
                await this.SendNotificationAsync(
                    NotificationMethods.InitializedNotification,
                    new InitializedNotificationParams(),
                    McpJsonUtilities.JsonContext.Default.InitializedNotificationParams,
                    cancellationToken: initializationCts.Token).ConfigureAwait(false);

            }
            catch (OperationCanceledException oce) when (initializationCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                LogClientInitializationTimeout(_endpointName);
                throw new TimeoutException("Initialization timed out", oce);
            }
        }
        catch (Exception e)
        {
            LogClientInitializationError(_endpointName, e);
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }

        LogClientConnected(_endpointName);
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

        await _sessionHandler.DisposeAsync().ConfigureAwait(false);
        await _transport.DisposeAsync().ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} client received server '{ServerInfo}' capabilities: '{Capabilities}'.")]
    private partial void LogServerCapabilitiesReceived(string endpointName, string capabilities, string serverInfo);

    [LoggerMessage(Level = LogLevel.Error, Message = "{EndpointName} client initialization error.")]
    private partial void LogClientInitializationError(string endpointName, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "{EndpointName} client initialization timed out.")]
    private partial void LogClientInitializationTimeout(string endpointName);

    [LoggerMessage(Level = LogLevel.Error, Message = "{EndpointName} client protocol version mismatch with server. Expected '{Expected}', received '{Received}'.")]
    private partial void LogServerProtocolVersionMismatch(string endpointName, string expected, string received);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} client created and connected.")]
    private partial void LogClientConnected(string endpointName);
}
