using System.Threading.Channels;
using Microsoft.Extensions.Logging;
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
/// <see cref="SendMessageAsync(JsonRpcMessage, CancellationToken)"/> and <see cref="DisposeAsync()"/> methods
/// to handle the specific transport mechanism being used.
/// </para>
/// </remarks>
public abstract partial class TransportBase : ITransport
{
    private readonly Channel<JsonRpcMessage> _messageChannel;
    private readonly ILogger _logger;
    private int _isConnected;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransportBase"/> class.
    /// </summary>
    protected TransportBase(string name, ILoggerFactory? loggerFactory)
    {
        Name = name;
        _logger = loggerFactory?.CreateLogger(GetType()) ?? NullLogger.Instance;

        // Unbounded channel to prevent blocking on writes
        _messageChannel = Channel.CreateUnbounded<JsonRpcMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>Gets the logger used by this transport.</summary>
    private protected ILogger Logger => _logger;

    /// <summary>
    /// Gets the name that identifies this transport endpoint in logs.
    /// </summary>
    /// <remarks>
    /// This name is used in log messages to identify the source of transport-related events.
    /// </remarks>
    protected string Name { get; }

    /// <inheritdoc/>
    public bool IsConnected => _isConnected == 1;

    /// <inheritdoc/>
    public ChannelReader<JsonRpcMessage> MessageReader => _messageChannel.Reader;

    /// <inheritdoc/>
    public abstract Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public abstract ValueTask DisposeAsync();

    /// <summary>
    /// Writes a message to the message channel.
    /// </summary>
    /// <param name="message">The message to write.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    protected async Task WriteMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Transport is not connected");
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var messageId = (message as JsonRpcMessageWithId)?.Id.ToString() ?? "(no id)";
            LogTransportReceivedMessage(Name, messageId);
        }

        await _messageChannel.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "{EndpointName} transport connect failed.")]
    private protected partial void LogTransportConnectFailed(string endpointName, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "{EndpointName} transport send failed for message ID '{MessageId}'.")]
    private protected partial void LogTransportSendFailed(string endpointName, string messageId, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} transport reading messages.")]
    private protected partial void LogTransportEnteringReadMessagesLoop(string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} transport completed reading messages.")]
    private protected partial void LogTransportEndOfStream(string endpointName);

    [LoggerMessage(Level = LogLevel.Trace, Message = "{EndpointName} transport received message. Message: '{Message}'.")]
    private protected partial void LogTransportReceivedMessageSensitive(string endpointName, string message);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{EndpointName} transport received message with ID '{MessageId}'.")]
    private protected partial void LogTransportReceivedMessage(string endpointName, string messageId);

    [LoggerMessage(Level = LogLevel.Trace, Message = "{EndpointName} transport received unexpected message. Message: '{Message}'.")]
    private protected partial void LogTransportMessageParseUnexpectedTypeSensitive(string endpointName, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} transport message parsing failed.")]
    private protected partial void LogTransportMessageParseFailed(string endpointName, Exception exception);

    [LoggerMessage(Level = LogLevel.Trace, Message = "{EndpointName} transport message parsing failed. Message: '{Message}'.")]
    private protected partial void LogTransportMessageParseFailedSensitive(string endpointName, string message, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} transport message reading canceled.")]
    private protected partial void LogTransportReadMessagesCancelled(string endpointName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{EndpointName} transport message reading failed.")]
    private protected partial void LogTransportReadMessagesFailed(string endpointName, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} shutting down.")]
    private protected partial void LogTransportShuttingDown(string endpointName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{EndpointName} shutdown failed.")]
    private protected partial void LogTransportShutdownFailed(string endpointName, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{EndpointName} shutdown failed waiting for message reading completion.")]
    private protected partial void LogTransportCleanupReadTaskFailed(string endpointName, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} shut down.")]
    private protected partial void LogTransportShutDown(string endpointName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{EndpointName} received message before connected.")]
    private protected partial void LogTransportMessageReceivedBeforeConnected(string endpointName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{EndpointName} endpoint event received out of order.")]
    private protected partial void LogTransportEndpointEventInvalid(string endpointName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{EndpointName} failed to parse event.")]
    private protected partial void LogTransportEndpointEventParseFailed(string endpointName, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{EndpointName} failed to parse event. Message: '{Message}'.")]
    private protected partial void LogTransportEndpointEventParseFailedSensitive(string endpointName, string message, Exception exception);
}