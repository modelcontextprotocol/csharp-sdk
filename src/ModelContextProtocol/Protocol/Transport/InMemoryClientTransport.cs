using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Messages;

using System.Threading.Channels;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Provides an in-memory implementation of the MCP client transport.
/// </summary>
public sealed class InMemoryClientTransport : TransportBase, IClientTransport
{
    private readonly string _endpointName = "InMemoryClientTransport";
    private readonly ILogger _logger;
    private readonly ChannelWriter<IJsonRpcMessage> _outgoingChannel;
    private readonly ChannelReader<IJsonRpcMessage> _incomingChannel;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _readTask;
    private SemaphoreSlim _connectLock = new SemaphoreSlim(1, 1);
    private volatile bool _disposed;

    /// <summary>
    /// Gets or sets the server transport this client connects to.
    /// </summary>
    internal InMemoryServerTransport? ServerTransport { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryClientTransport"/> class.
    /// </summary>
    /// <param name="loggerFactory">Optional logger factory for logging transport operations.</param>
    /// <param name="outgoingChannel">Channel for sending messages to the server.</param>
    /// <param name="incomingChannel">Channel for receiving messages from the server.</param>
    internal InMemoryClientTransport(
        ILoggerFactory? loggerFactory,
        ChannelWriter<IJsonRpcMessage> outgoingChannel,
        ChannelReader<IJsonRpcMessage> incomingChannel)
        : base(loggerFactory)
    {
        _logger = loggerFactory?.CreateLogger<InMemoryClientTransport>()
                ?? NullLogger<InMemoryClientTransport>.Instance;
        _outgoingChannel = outgoingChannel;
        _incomingChannel = incomingChannel;
    }



    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (IsConnected)
            {
                _logger.TransportAlreadyConnected(_endpointName);
                throw new McpTransportException("Transport is already connected");
            }

            _logger.TransportConnecting(_endpointName);

            try
            {
                // Start the server if it exists and is not already connected
                if (ServerTransport != null && !ServerTransport.IsConnected)
                {
                    await ServerTransport.StartListeningAsync(cancellationToken).ConfigureAwait(false);
                }

                _cancellationTokenSource = new CancellationTokenSource();
                _readTask = Task.Run(() => ReadMessagesAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

                SetConnected(true);
            }
            catch (Exception ex)
            {
                _logger.TransportConnectFailed(_endpointName, ex);
                await CleanupAsync(cancellationToken).ConfigureAwait(false);
                throw new McpTransportException("Failed to connect transport", ex);
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <inheritdoc/>
    public override async Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

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
            await _outgoingChannel.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            _logger.TransportSentMessage(_endpointName, id);
        }
        catch (Exception ex)
        {
            _logger.TransportSendFailed(_endpointName, id, ex);
            throw new McpTransportException("Failed to send message", ex);
        }
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await CleanupAsync(CancellationToken.None).ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private async Task ReadMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.TransportEnteringReadMessagesLoop(_endpointName);

            await foreach (var message in _incomingChannel.ReadAllAsync(cancellationToken))
            {
                string id = "(no id)";
                if (message is IJsonRpcMessageWithId messageWithId)
                {
                    id = messageWithId.Id.ToString();
                }

                _logger.TransportReceivedMessageParsed(_endpointName, id);
                
                // Write to the base class's message channel that's exposed via MessageReader
                await WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);
                
                _logger.TransportMessageWritten(_endpointName, id);
            }

            _logger.TransportExitingReadMessagesLoop(_endpointName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.TransportReadMessagesCancelled(_endpointName);
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.TransportReadMessagesFailed(_endpointName, ex);
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _logger.TransportCleaningUp(_endpointName);

        try
        {
            if (_cancellationTokenSource != null)
            {
                await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }

            if (_readTask != null)
            {
                try
                {
                    _logger.TransportWaitingForReadTask(_endpointName);
                    await _readTask.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    _logger.TransportCleanupReadTaskTimeout(_endpointName);
                }
                catch (OperationCanceledException)
                {
                    _logger.TransportCleanupReadTaskCancelled(_endpointName);
                }
                catch (Exception ex)
                {
                    _logger.TransportCleanupReadTaskFailed(_endpointName, ex);
                }
                finally
                {
                    _readTask = null;
                }
            }

            _connectLock.Dispose();
        }
        finally
        {
            SetConnected(false);
            _logger.TransportCleanedUp(_endpointName);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(InMemoryClientTransport));
        }
    }
}