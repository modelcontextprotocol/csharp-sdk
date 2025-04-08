using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Provides a server-side MCP transport implementation using a pair of input and output streams.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="StreamServerTransport"/> class implements bidirectional JSON-RPC messaging over arbitrary
/// streams, allowing MCP communication with clients through various I/O channels such as network sockets,
/// memory streams, or file streams.
/// </para>
/// <para>
/// This transport reads JSON-RPC messages from the input stream and writes responses to the output stream,
/// with each message separated by a newline character. It handles connection management, message parsing,
/// and serialization automatically.
/// </para>
/// <para>
/// This implementation is commonly used for server-side handling of MCP connections where the protocol
/// doesn't dictate a specific transport mechanism, or when integrating with existing stream-based systems.
/// </para>
/// </remarks>
public class StreamServerTransport : TransportBase, ITransport
{
    private static readonly byte[] s_newlineBytes = "\n"u8.ToArray();

    private readonly ILogger _logger;

    private readonly TextReader _inputReader;
    private readonly Stream _outputStream;
    private readonly string _endpointName;

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();

    private readonly Task _readLoopCompleted;
    private int _disposed = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamServerTransport"/> class with explicit input/output streams.
    /// </summary>
    /// <param name="inputStream">The input <see cref="Stream"/> to use as standard input.</param>
    /// <param name="outputStream">The output <see cref="Stream"/> to use as standard output.</param>
    /// <param name="serverName">Optional name of the server, used for diagnostic purposes, like logging.</param>
    /// <param name="loggerFactory">Optional logger factory used for logging employed by the transport.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inputStream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="outputStream"/> is <see langword="null"/>.</exception>
    public StreamServerTransport(Stream inputStream, Stream outputStream, string? serverName = null, ILoggerFactory? loggerFactory = null)
        : base(loggerFactory)
    {
        Throw.IfNull(inputStream);
        Throw.IfNull(outputStream);

        _logger = loggerFactory?.CreateLogger(GetType()) ?? NullLogger.Instance;

        _inputReader = new StreamReader(inputStream, Encoding.UTF8);
        _outputStream = outputStream;

        SetConnected(true);
        _readLoopCompleted = Task.Run(ReadMessagesAsync, _shutdownCts.Token);

        _endpointName = serverName is not null ? $"Server (stream) ({serverName})" : "Server (stream)";
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// This server-side stream implementation serializes the JSON-RPC message to the underlying
    /// output stream in a thread-safe manner using a semaphore lock. The serialization format uses 
    /// the LSP (Language Server Protocol) style with Content-Length headers:
    /// <list type="bullet">
    ///   <item>A Content-Length header that specifies the byte length of the JSON message</item>
    ///   <item>A blank line separator</item>
    ///   <item>The UTF-8 encoded JSON representation of the message</item>
    /// </list>
    /// </para>
    /// <para>
    /// The method includes proper error handling for disconnected transports and serialization errors,
    /// and logs the message details at appropriate log levels for debugging purposes.
    /// </para>
    /// </remarks>
    /// <exception cref="McpTransportException">Thrown when the transport is not connected.</exception>
    public override async Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            _logger.TransportNotConnected(_endpointName);
            throw new McpTransportException("Transport is not connected");
        }

        using var _ = await _sendLock.LockAsync(cancellationToken).ConfigureAwait(false);

        string id = "(no id)";
        if (message is IJsonRpcMessageWithId messageWithId)
        {
            id = messageWithId.Id.ToString();
        }

        try
        {
            _logger.TransportSendingMessage(_endpointName, id);

            await JsonSerializer.SerializeAsync(_outputStream, message, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IJsonRpcMessage)), cancellationToken).ConfigureAwait(false);
            await _outputStream.WriteAsync(s_newlineBytes, cancellationToken).ConfigureAwait(false);
            await _outputStream.FlushAsync(cancellationToken).ConfigureAwait(false);;

            _logger.TransportSentMessage(_endpointName, id);
        }
        catch (Exception ex)
        {
            _logger.TransportSendFailed(_endpointName, id, ex);
            throw new McpTransportException("Failed to send message", ex);
        }
    }

    private async Task ReadMessagesAsync()
    {
        CancellationToken shutdownToken = _shutdownCts.Token;
        try
        {
            _logger.TransportEnteringReadMessagesLoop(_endpointName);

            while (!shutdownToken.IsCancellationRequested)
            {
                _logger.TransportWaitingForMessage(_endpointName);

                var line = await _inputReader.ReadLineAsync(shutdownToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (line is null)
                    {
                        _logger.TransportEndOfStream(_endpointName);
                        break;
                    }

                    continue;
                }

                _logger.TransportReceivedMessage(_endpointName, line);
                _logger.TransportMessageBytesUtf8(_endpointName, line);

                try
                {
                    if (JsonSerializer.Deserialize(line, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IJsonRpcMessage))) is IJsonRpcMessage message)
                    {
                        string messageId = "(no id)";
                        if (message is IJsonRpcMessageWithId messageWithId)
                        {
                            messageId = messageWithId.Id.ToString();
                        }
                        _logger.TransportReceivedMessageParsed(_endpointName, messageId);

                        await WriteMessageAsync(message, shutdownToken).ConfigureAwait(false);
                        _logger.TransportMessageWritten(_endpointName, messageId);
                    }
                    else
                    {
                        _logger.TransportMessageParseUnexpectedType(_endpointName, line);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.TransportMessageParseFailed(_endpointName, line, ex);
                    // Continue reading even if we fail to parse a message
                }
            }

            _logger.TransportExitingReadMessagesLoop(_endpointName);
        }
        catch (OperationCanceledException)
        {
            _logger.TransportReadMessagesCancelled(_endpointName);
        }
        catch (Exception ex)
        {
            _logger.TransportReadMessagesFailed(_endpointName, ex);
        }
        finally
        {
            SetConnected(false);
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// Asynchronously releases all resources used by the stream server transport.
    /// </summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    /// <remarks>
    /// This method cancels ongoing operations, disposes the input/output streams, 
    /// and waits for the read task to complete. Multiple calls to this method will only
    /// dispose the resources once.
    /// 
    /// After disposal, <see cref="IsConnected"/> will return false and any attempt 
    /// to send messages will throw an <see cref="McpTransportException"/>.
    /// </remarks>
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _logger.TransportCleaningUp(_endpointName);

            // Signal to the stdin reading loop to stop.
            await _shutdownCts.CancelAsync().ConfigureAwait(false);
            _shutdownCts.Dispose();

            // Dispose of stdin/out. Cancellation may not be able to wake up operations
            // synchronously blocked in a syscall; we need to forcefully close the handle / file descriptor.
            _inputReader?.Dispose();
            _outputStream?.Dispose();

            // Make sure the work has quiesced.
            try
            {
                _logger.TransportWaitingForReadTask(_endpointName);
                await _readLoopCompleted.ConfigureAwait(false);
                _logger.TransportReadTaskCleanedUp(_endpointName);
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
        }
        finally
        {
            SetConnected(false);
            _logger.TransportCleanedUp(_endpointName);
        }

        GC.SuppressFinalize(this);
    }
}
