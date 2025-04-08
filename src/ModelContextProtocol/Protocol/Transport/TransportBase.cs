using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Messages;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Base class for implementing MCP transports with common functionality.
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
/// <para>
/// Implementations must manage their connection state by calling <see cref="SetConnected(bool)"/> 
/// when appropriate, and must properly dispose of resources in their <see cref="DisposeAsync()"/> implementation.
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
    /// <summary>
    /// Sends a JSON-RPC message through the transport.
    /// </summary>
    /// <param name="message">The JSON-RPC message to send. This can be any type that implements IJsonRpcMessage.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// <para>
    /// This abstract method must be implemented by derived classes to handle the actual transmission
    /// of messages using the specific transport mechanism (e.g., HTTP, WebSockets, streams).
    /// </para>
    /// <para>
    /// Implementations should:
    /// <list type="bullet">
    ///   <item>Check <see cref="IsConnected"/> and throw an appropriate exception if the transport is not connected</item>
    ///   <item>Serialize the message to the appropriate format for the transport</item>
    ///   <item>Send the serialized message through the transport channel</item>
    ///   <item>Handle any transport-specific errors and throw appropriate exceptions</item>
    /// </list>
    /// </para>
    /// </remarks>

    /// <inheritdoc/>
    /// <summary>
    /// Asynchronously releases all resources used by the transport.
    /// </summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    /// <remarks>
    /// Implementations should dispose of all connections, handlers, and other resources.
    /// Once disposed, the transport is no longer usable and <see cref="IsConnected"/> should return false.
    /// Implementations should set <see cref="IsConnected"/> to false using <see cref="SetConnected(bool)"/>
    /// as part of their disposal process.
    /// Multiple calls to this method should be handled gracefully.
    /// </remarks>
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
    /// <remarks>
    /// <para>
    /// This method is used by transport implementations to update their connection status.
    /// When a transport connects to its communication channel, it should call <c>SetConnected(true)</c>.
    /// When the connection is closed or lost, it should call <c>SetConnected(false)</c>.
    /// </para>
    /// <para>
    /// When the connection state changes from connected to disconnected, the message channel writer
    /// is completed, which allows consumers of the message channel to detect that no more messages
    /// will be received.
    /// </para>
    /// <para>
    /// Transport implementations should call this method at appropriate points in their lifecycle:
    /// - When a connection is established (e.g., WebSocket connected)
    /// - When a connection is terminated (e.g., stream closed)
    /// - When handling errors that result in connection loss
    /// - During disposal
    /// </para>
    /// </remarks>
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