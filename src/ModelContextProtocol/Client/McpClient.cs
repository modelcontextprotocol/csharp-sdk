using Microsoft.Extensions.Logging;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Shared;
using ModelContextProtocol.Utils.Json;
using System.Text.Json;

namespace ModelContextProtocol.Client;

/// <inheritdoc/>
internal sealed class McpClient : McpEndpoint, IMcpClient
{
    private static Implementation DefaultImplementation { get; } = new()
    {
        Name = DefaultAssemblyName.Name ?? nameof(McpClient),
        Version = DefaultAssemblyName.Version?.ToString() ?? "1.0.0",
    };

    private readonly IClientTransport _clientTransport;
    private readonly McpClientOptions _options;

    private ITransport? _sessionTransport;
    private CancellationTokenSource? _connectCts;

    private ServerCapabilities? _serverCapabilities;
    private Implementation? _serverInfo;
    private string? _serverInstructions;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpClient"/> class.
    /// </summary>
    /// <param name="clientTransport">The transport to use for communication with the server.</param>
    /// <param name="options">Options for the client, defining protocol version and capabilities.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public McpClient(IClientTransport clientTransport, McpClientOptions? options, ILoggerFactory? loggerFactory)
        : base(loggerFactory)
    {
        options ??= new();

        _clientTransport = clientTransport;
        _options = options;

        EndpointName = clientTransport.Name;

        if (options.Capabilities is { } capabilities)
        {
            if (capabilities.NotificationHandlers is { } notificationHandlers)
            {
                NotificationHandlers.RegisterRange(notificationHandlers);
            }

            if (capabilities.Sampling is { } samplingCapability)
            {
                if (samplingCapability.SamplingHandler is not { } samplingHandler)
                {
                    throw new InvalidOperationException($"Sampling capability was set but it did not provide a handler.");
                }

                RequestHandlers.Set(
                    RequestMethods.SamplingCreateMessage,
                    (request, cancellationToken) => samplingHandler(
                        request,
                        request?.Meta?.ProgressToken is { } token ? new TokenProgress(this, token) : NullProgress.Instance,
                        cancellationToken),
                    McpJsonUtilities.JsonContext.Default.CreateMessageRequestParams,
                    McpJsonUtilities.JsonContext.Default.CreateMessageResult);
            }

            if (capabilities.Roots is { } rootsCapability)
            {
                if (rootsCapability.RootsHandler is not { } rootsHandler)
                {
                    throw new InvalidOperationException($"Roots capability was set but it did not provide a handler.");
                }

                RequestHandlers.Set(
                    RequestMethods.RootsList,
                    rootsHandler,
                    McpJsonUtilities.JsonContext.Default.ListRootsRequestParams,
                    McpJsonUtilities.JsonContext.Default.ListRootsResult);
            }
        }
    }

    /// <inheritdoc/>
    public ServerCapabilities ServerCapabilities => _serverCapabilities ?? throw new InvalidOperationException("The client is not connected.");

    /// <inheritdoc/>
    public Implementation ServerInfo => _serverInfo ?? throw new InvalidOperationException("The client is not connected.");

    /// <inheritdoc/>
    public string? ServerInstructions => _serverInstructions;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// For clients, the endpoint name is formatted as "Client ({ServerConfig.Id}: {ServerConfig.Name})".
    /// </para>
    /// <para>
    /// This property is initialized during construction and remains constant throughout the client's lifetime.
    /// It's used in all logging operations to identify this specific client connection.
    /// </para>
    /// </remarks>
    public override string EndpointName { get; }

    /// <summary>
    /// Asynchronously connects to an MCP server, establishes the transport connection, and completes the initialization handshake.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous connect operation.</returns>
    /// <exception cref="McpException">Thrown when initialization fails, times out, or if the server's protocol version doesn't match the expected version.</exception>
    /// <exception cref="McpTransportException">Thrown when the transport connection fails.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the client is already connected.</exception>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationToken = _connectCts.Token;

        try
        {
            // Connect transport
            _sessionTransport = await _clientTransport.ConnectAsync(cancellationToken).ConfigureAwait(false);
            InitializeSession(_sessionTransport);
            // We don't want the ConnectAsync token to cancel the session after we've successfully connected.
            // The base class handles cleaning up the session in DisposeAsync without our help.
            StartSession(_sessionTransport, fullSessionCancellationToken: CancellationToken.None);

            // Perform initialization sequence
            using var initializationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            initializationCts.CancelAfter(_options.InitializationTimeout);

            try
            {
                // Send initialize request
                var initializeResponse = await this.SendRequestAsync(
                    RequestMethods.Initialize,
                    new InitializeRequestParams
                    {
                        ProtocolVersion = _options.ProtocolVersion,
                        Capabilities = _options.Capabilities ?? new ClientCapabilities(),
                        ClientInfo = _options.ClientInfo ?? DefaultImplementation,
                    },
                    McpJsonUtilities.JsonContext.Default.InitializeRequestParams,
                    McpJsonUtilities.JsonContext.Default.InitializeResult,
                    cancellationToken: initializationCts.Token).ConfigureAwait(false);

                // Store server information
                _logger.ServerCapabilitiesReceived(EndpointName,
                    capabilities: JsonSerializer.Serialize(initializeResponse.Capabilities, McpJsonUtilities.JsonContext.Default.ServerCapabilities),
                    serverInfo: JsonSerializer.Serialize(initializeResponse.ServerInfo, McpJsonUtilities.JsonContext.Default.Implementation));

                _serverCapabilities = initializeResponse.Capabilities;
                _serverInfo = initializeResponse.ServerInfo;
                _serverInstructions = initializeResponse.Instructions;

                // Validate protocol version
                if (initializeResponse.ProtocolVersion != _options.ProtocolVersion)
                {
                    _logger.ServerProtocolVersionMismatch(EndpointName, _options.ProtocolVersion, initializeResponse.ProtocolVersion);
                    throw new McpException($"Server protocol version mismatch. Expected {_options.ProtocolVersion}, got {initializeResponse.ProtocolVersion}");
                }

                // Send initialized notification
                await SendMessageAsync(
                    new JsonRpcNotification { Method = NotificationMethods.InitializedNotification },
                    initializationCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException oce) when (initializationCts.IsCancellationRequested)
            {
                _logger.ClientInitializationTimeout(EndpointName);
                throw new McpException("Initialization timed out", oce);
            }
        }
        catch (Exception e)
        {
            _logger.ClientInitializationError(EndpointName, e);
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc/>
    /// <summary>
    /// Asynchronously releases resources used by the MCP client without any synchronization.
    /// </summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    /// <remarks>
    /// This method:
    /// <list type="bullet">
    /// <item><description>Cancels and disposes the connection cancellation token source</description></item>
    /// <item><description>Calls the base class implementation to clean up common endpoint resources</description></item>
    /// <item><description>Disposes the session transport</description></item>
    /// </list>
    /// </remarks>
    public override async ValueTask DisposeUnsynchronizedAsync()
    {
        try
        {
            if (_connectCts is not null)
            {
                await _connectCts.CancelAsync().ConfigureAwait(false);
                _connectCts.Dispose();
            }

            await base.DisposeUnsynchronizedAsync().ConfigureAwait(false);
        }
        finally
        {
            if (_sessionTransport is not null)
            {
                await _sessionTransport.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
