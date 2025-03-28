using ModelContextProtocol.Configuration;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Shared;
using ModelContextProtocol.Utils.Json;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ModelContextProtocol.Client;

/// <inheritdoc/>
internal sealed class McpClient : McpJsonRpcEndpoint, IMcpClient
{
    private readonly McpClientOptions _options;
    private readonly IClientTransport _clientTransport;

    private volatile bool _isInitializing;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpClient"/> class.
    /// </summary>
    /// <param name="transport">The transport to use for communication with the server.</param>
    /// <param name="options">Options for the client, defining protocol version and capabilities.</param>
    /// <param name="serverConfig">The server configuration.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public McpClient(IClientTransport transport, McpClientOptions options, McpServerConfig serverConfig, ILoggerFactory? loggerFactory)
        : base(transport, loggerFactory)
    {
        _options = options;
        _clientTransport = transport;

        EndpointName = $"Client ({serverConfig.Id}: {serverConfig.Name})";

        if (options.Capabilities?.Sampling is { } samplingCapability)
        {
            if (samplingCapability.SamplingHandler is not { } samplingHandler)
            {
                throw new InvalidOperationException($"Sampling capability was set but it did not provide a handler.");
            }

            SetRequestHandler<CreateMessageRequestParams, CreateMessageResult>(
                "sampling/createMessage",
                (request, ct) => samplingHandler(request, ct));
        }

        if (options.Capabilities?.Roots is { } rootsCapability)
        {
            if (rootsCapability.RootsHandler is not { } rootsHandler)
            {
                throw new InvalidOperationException($"Roots capability was set but it did not provide a handler.");
            }

            SetRequestHandler<ListRootsRequestParams, ListRootsResult>(
                "roots/list",
                (request, ct) => rootsHandler(request, ct));
        }
    }

    /// <inheritdoc/>
    public ServerCapabilities? ServerCapabilities { get; private set; }

    /// <inheritdoc/>
    public Implementation? ServerInfo { get; private set; }

    /// <inheritdoc/>
    public string? ServerInstructions { get; private set; }

    /// <inheritdoc/>
    public override string EndpointName { get; }

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsInitialized)
        {
            _logger.ClientAlreadyInitialized(EndpointName);
            return;
        }

        if (_isInitializing)
        {
            _logger.ClientAlreadyInitializing(EndpointName);
            throw new InvalidOperationException("Client is already initializing");
        }

        _isInitializing = true;
        try
        {
            CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Connect transport
            await _clientTransport.ConnectAsync(CancellationTokenSource.Token).ConfigureAwait(false);

            // Start processing messages
            MessageProcessingTask = ProcessMessagesAsync(CancellationTokenSource.Token);

            // Perform initialization sequence
            await InitializeAsync(CancellationTokenSource.Token).ConfigureAwait(false);

            IsInitialized = true;
        }
        catch (Exception e)
        {
            _logger.ClientInitializationError(EndpointName, e);
            await CleanupAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var initializationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        initializationCts.CancelAfter(_options.InitializationTimeout);

        try
        {
            // Send initialize request
            var initializeResponse = await SendRequestAsync<InitializeResult>(
                new JsonRpcRequest
                {
                    Method = "initialize",
                    Params = new InitializeRequestParams()
                    {
                        ProtocolVersion = _options.ProtocolVersion,
                        Capabilities = _options.Capabilities ?? new ClientCapabilities(),
                        ClientInfo = _options.ClientInfo
                    }
                },
                initializationCts.Token).ConfigureAwait(false);

            // Store server information
            _logger.ServerCapabilitiesReceived(EndpointName, 
                capabilities: JsonSerializer.Serialize(initializeResponse.Capabilities, McpJsonUtilities.JsonContext.Default.ServerCapabilities),
                serverInfo: JsonSerializer.Serialize(initializeResponse.ServerInfo, McpJsonUtilities.JsonContext.Default.Implementation));

            ServerCapabilities = initializeResponse.Capabilities;
            ServerInfo = initializeResponse.ServerInfo;
            ServerInstructions = initializeResponse.Instructions;

            // Validate protocol version
            if (initializeResponse.ProtocolVersion != _options.ProtocolVersion)
            {
                _logger.ServerProtocolVersionMismatch(EndpointName, _options.ProtocolVersion, initializeResponse.ProtocolVersion);
                throw new McpClientException($"Server protocol version mismatch. Expected {_options.ProtocolVersion}, got {initializeResponse.ProtocolVersion}");
            }

            // Send initialized notification
            await SendMessageAsync(
                new JsonRpcNotification { Method = "notifications/initialized" },
                initializationCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (initializationCts.IsCancellationRequested)
        {
            _logger.ClientInitializationTimeout(EndpointName);
            throw new McpClientException("Initialization timed out");
        }
    }
}
