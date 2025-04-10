using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Messages;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Provides a base class for implementing <see cref="ITransport"/>.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="TransportBase"/> class provides core functionality required by most <see cref="ITransport"/>
/// implementations, including message channel management, connection state tracking, and logging support.
/// </para>
/// <para>
/// Custom transport implementations should inherit from this class and implement the abstract
/// <see cref="SendMessageAsync(IJsonRpcMessage, CancellationToken)"/> and <see cref="DisposeAsync()"/> methods
/// to handle the specific transport mechanism being used.
/// </para>
/// </remarks>
public abstract class TransportBase : ITransport
{
    private readonly Channel<IJsonRpcMessage> _messageChannel;
    private readonly ILogger _logger;
    private int _isConnected;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransportBase"/> class.
    /// </summary>
    protected TransportBase(ILoggerFactory? loggerFactory)
    {
        // Unbounded channel to prevent blocking on writes
        _messageChannel = Channel.CreateUnbounded<IJsonRpcMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });
        _logger = loggerFactory?.CreateLogger(GetType()) ?? NullLogger.Instance;
    }

    /// <inheritdoc/>
    public bool IsConnected => _isConnected == 1;

    /// <inheritdoc/>
    public ChannelReader<IJsonRpcMessage> MessageReader => _messageChannel.Reader;

    /// <inheritdoc/>
    public abstract Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public abstract ValueTask DisposeAsync();

    /// <summary>
    /// Writes a message to the message channel.
    /// </summary>
    /// <param name="message">The message to write.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    protected async Task WriteMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            throw new McpTransportException("Transport is not connected");
        }

        _logger.TransportWritingMessageToChannel(message);
        await _messageChannel.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        _logger.TransportMessageWrittenToChannel();
    }

    /// <summary>
    /// Sets the connected state of the transport.
    /// </summary>
    /// <param name="isConnected">Whether the transport is connected.</param>
    protected void SetConnected(bool isConnected)
    {
        var newIsConnected = isConnected ? 1 : 0;
        if (Interlocked.Exchange(ref _isConnected, newIsConnected) == newIsConnected)
        {
            return;
        }

        if (!isConnected)
        {
            _messageChannel.Writer.Complete();
        }
    }
}