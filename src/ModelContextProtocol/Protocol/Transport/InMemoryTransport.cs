using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using ModelContextProtocol.Configuration;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Server;

using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;


namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Provides an in memory implementation of the MCP transport protocol over shared memory.
/// This transport enables efficient in-process MCP functionality and easier test authoring.
/// </summary>
/// <remarks>
/// <para>
/// The InMemoryTransport allows both client and server to communicate within the same process,
/// which is particularly useful for testing scenarios and embedded applications.
/// </para>
/// <para>
/// This implementation requires dynamic code access for tool registration and might not work in Native AOT.
/// </para>
/// </remarks>
[RequiresUnreferencedCode(RequiresUnreferencedCodeMessage)]
public sealed class InMemoryTransport : TransportBase, IServerTransport, IClientTransport
{
    private const string RequiresUnreferencedCodeMessage = "This method requires dynamic lookup of method metadata and might not work in Native AOT.";

    private readonly string _endpointName = "InMemoryTransport";
    private readonly ILogger _logger;
    private readonly Type[] _toolTypes;
    private readonly Channel<IJsonRpcMessage> _sharedChannel;
    private readonly SemaphoreSlim _connectionLock;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _processingTask;
    private Task? _serverTask;
    private IMcpServer? _server;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryTransport"/> class.
    /// </summary>
    /// <param name="loggerFactory">Optional logger factory for logging transport operations.</param>
    /// <param name="toolTypes">The tool types to be registered with the transport.</param>
    /// <exception cref="ArgumentException">Thrown when no tool types are provided.</exception>
    private InMemoryTransport(
        ILoggerFactory? loggerFactory,
        IEnumerable<Type> toolTypes)
        : base(loggerFactory)
    {
        var arrayOfToolTypes = toolTypes as Type[] ?? toolTypes.ToArray();
        if (arrayOfToolTypes.Length == 0)
        {
            throw new ArgumentException(
                "At least one tool type must be provided",
                nameof(toolTypes));
        }

        _toolTypes = arrayOfToolTypes;
        _logger = loggerFactory?.CreateLogger<InMemoryTransport>()
            ?? NullLogger<InMemoryTransport>.Instance;
        _connectionLock = new SemaphoreSlim(1, 1);

        var options = new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true
        };
        _sharedChannel = Channel.CreateUnbounded<IJsonRpcMessage>(options);
    }

    /// <summary>
    /// Creates a new instance of the <see cref="InMemoryTransport"/> class.
    /// </summary>
    /// <param name="loggerFactory">Optional logger factory for logging transport operations.</param>
    /// <param name="toolTypes">One or more tool types to be registered with the transport.</param>
    /// <returns>A new instance of <see cref="InMemoryTransport"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when no tool types are provided.</exception>
    /// <exception cref="ArgumentNullException">Thrown when tool types is null.</exception>
    [RequiresUnreferencedCode(RequiresUnreferencedCodeMessage)]
    public static InMemoryTransport Create(
        ILoggerFactory? loggerFactory,
        params IEnumerable<Type>[] toolTypes)
    {
        if (toolTypes is null)
        {
            throw new ArgumentNullException(nameof(toolTypes));
        }

        if (toolTypes.Length == 0)
        {
            throw new ArgumentException(
                "At least one tool type enumerable must be provided",
                nameof(toolTypes));
        }

        return new InMemoryTransport(
            loggerFactory,
            toolTypes.SelectMany(x => x));
    }

    /// <inheritdoc />
    public override async Task SendMessageAsync(
        IJsonRpcMessage message,
        CancellationToken cancellationToken = default)
    {
        // During disposal, allow final message to complete if we're still connected
        if (_disposed && !IsConnected)
        {
            throw new ObjectDisposedException(nameof(InMemoryTransport));
        }

        if (!IsConnected)
        {
            _logger.TransportNotConnected(_endpointName);
            throw new McpTransportException("Transport is not connected");
        }

        string id = "(no id)";
        if (message is IJsonRpcMessageWithId messageWithId)
        {
            id = messageWithId.Id.ToString();
        }

        try
        {
            _logger.TransportSendingMessage(_endpointName, id);

            // Only write to shared channel, let HandleMessageReceivedAsync handle base channel write
            await _sharedChannel.Writer.WriteAsync(message, cancellationToken);

            // Wait briefly for the message to be processed if we're disposing
            if (_disposed)
            {
                await Task.Delay(50, cancellationToken);
            }

            _logger.TransportSentMessage(_endpointName, id);
        }
        catch (Exception ex)
        {
            _logger.TransportSendFailed(_endpointName, id, ex);
            throw new McpTransportException("Failed to send message", ex);
        }
    }

    /// <inheritdoc />
    Task IClientTransport.ConnectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
#pragma warning disable IL2026
        return ConnectInternalAsync(cancellationToken);
#pragma warning restore IL2026
    }

    /// <inheritdoc />
    Task IServerTransport.StartListeningAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
#pragma warning disable IL2026
        return ConnectInternalAsync(cancellationToken);
#pragma warning restore IL2026
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await CleanupAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Connects the transport and initializes the server.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="McpTransportException">Thrown when the transport is already connected or when connection fails.</exception>
    [RequiresUnreferencedCode(RequiresUnreferencedCodeMessage)]
    private async Task ConnectInternalAsync(
        CancellationToken cancellationToken)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected)
            {
                _logger.TransportAlreadyConnected(_endpointName);
                throw new McpTransportException("Transport is already connected");
            }

            _logger.TransportConnecting(_endpointName);

            // Create a service collection and builder to set up the in-memory server
            var services = new ServiceCollection();
            services.AddSingleton<IServerTransport>(this);
            services.AddSingleton<IClientTransport>(this);

            // Configure server options
            var serverOptions = new McpServerOptions
            {
                ServerInfo = new Protocol.Types.Implementation { Name = "InMemoryServer", Version = "1.0" },
                ProtocolVersion = "2024",
                Capabilities = new Protocol.Types.ServerCapabilities()
            };

            services.AddOptions<McpServerOptions>().Configure(options =>
            {
                options.ServerInfo = serverOptions.ServerInfo;
                options.ProtocolVersion = serverOptions.ProtocolVersion;
                options.Capabilities = serverOptions.Capabilities;
            });

            // Create a server builder to register the tools
            var builder = new DefaultMcpServerBuilder(services);

            // Register the provided tool types
            if (_toolTypes.Length > 0)
            {
                _logger.LogDebug("Registering {Count} tool types", _toolTypes.Length);
                builder.WithTools(_toolTypes);
            }

            try
            {
                // Create IServiceProvider instance manually
                var serviceProvider = services.BuildServiceProvider();

                // Create and initialize the server using a logger factory
                var loggerFactory = NullLoggerFactory.Instance;
                _server = McpServerFactory.Create(this, serverOptions, loggerFactory, serviceProvider);

                // Create cancellation source to manage all tasks
                _cancellationTokenSource = new CancellationTokenSource();

                // Start the server as fire-and-forget (don't await)
                _logger.LogDebug("Starting server (fire-and-forget)");
                _serverTask = _server.StartAsync(_cancellationTokenSource.Token);

                // Start a background task to process messages from the shared channel
                _processingTask = Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogDebug("Starting message processing task");
                        await foreach (var message in _sharedChannel.Reader.ReadAllAsync(_cancellationTokenSource.Token))
                        {
                            _logger.LogTrace("Transport reading message from channel: {Message}", message);
                            await HandleMessageReceivedAsync(message, _cancellationTokenSource.Token);
                        }
                    }
                    catch (OperationCanceledException) when (_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        // Expected when cancellation is requested
                        _logger.TransportReadMessagesCancelled(_endpointName);
                    }
                    catch (Exception ex)
                    {
                        _logger.TransportReadMessagesFailed(_endpointName, ex);
                    }
                }, _cancellationTokenSource.Token);

                // Short delay to allow background tasks to start
                await Task.Delay(10, cancellationToken);

                // Only set connected if initialization succeeded
                SetConnected(true);
                _logger.LogDebug("Transport connected for {EndpointName}", _endpointName);
            }
            catch (Exception ex)
            {
                _logger.TransportConnectFailed(_endpointName, ex);
                await CleanupAsync(cancellationToken);
                throw new McpTransportException("Failed to connect transport", ex);
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Cleans up resources used by the transport.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        _logger.TransportCleaningUp(_endpointName);

        try
        {
            // Mark as disposed to prevent new operations but keep connection alive
            _disposed = true;

            // First wait for the processing task to complete
            if (_processingTask != null)
            {
                try
                {
                    _logger.TransportWaitingForReadTask(_endpointName);
                    await Task.WhenAny(_processingTask, Task.Delay(500, cancellationToken));
                }
                catch (Exception ex)
                {
                    _logger.TransportCleanupReadTaskFailed(_endpointName, ex);
                }
            }

            // Complete the shared channel
            _sharedChannel.Writer.Complete();

            // Then cancel the server and tasks
            if (_cancellationTokenSource != null)
            {
                await _cancellationTokenSource.CancelAsync();
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
                _processingTask = null;
                _serverTask = null;
            }

            // Dispose server with timeout
            if (_server != null)
            {
                try
                {
                    await _server.DisposeAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Timeout is acceptable
                }
                catch (Exception ex)
                {
                    _logger.TransportShutdownFailed(_endpointName, ex);
                }
                _server = null;
            }

            // Dispose connection lock
            _connectionLock.Dispose();
        }
        finally
        {
            // Set connected to false last
            SetConnected(false);
            _logger.TransportCleanedUp(_endpointName);
        }
    }

    /// <summary>
    /// Throws an <see cref="ObjectDisposedException"/> if the transport has been disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the transport has been disposed.</exception>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(InMemoryTransport));
        }
    }

    /// <summary>
    /// Processes a message received from the shared channel.
    /// </summary>
    /// <param name="message">The message to process.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task HandleMessageReceivedAsync(IJsonRpcMessage message, CancellationToken cancellationToken)
    {
        string id = "(no id)";
        if (message is IJsonRpcMessageWithId messageWithId)
        {
            id = messageWithId.Id.ToString();
        }

        try
        {
            _logger.TransportReceivedMessageParsed(_endpointName, id);

            // Write the message to the base class message channel
            await base.WriteMessageAsync(message, cancellationToken);

            _logger.TransportMessageWritten(_endpointName, id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transport message write failed for {EndpointName} with ID {MessageId}", _endpointName, id);
        }
    }
}
