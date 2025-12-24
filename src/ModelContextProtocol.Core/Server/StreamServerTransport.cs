using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Core;
using ModelContextProtocol.Protocol;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides an <see cref="ITransport"/> implemented using a pair of input and output streams.
/// </summary>
/// <remarks>
/// The <see cref="StreamServerTransport"/> class implements bidirectional JSON-RPC messaging over arbitrary
/// streams, allowing MCP communication with clients through various I/O channels such as network sockets,
/// memory streams, or pipes.
/// </remarks>
public class StreamServerTransport : TransportBase
{
    private static readonly byte[] s_newlineBytes = "\n"u8.ToArray();

    private readonly ILogger _logger;

    private readonly Stream _inputStream;
    private readonly Stream _outputStream;

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    
    // Intentionally not disposed; once this transport instance is collectable, CTS finalization will clean up.
    private CancellationTokenSource _shutdownCts = new();

    private readonly Task _readLoopCompleted;
    private int _disposed = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamServerTransport"/> class with explicit input/output streams.
    /// </summary>
    /// <param name="inputStream">The input <see cref="Stream"/> to use as standard input.</param>
    /// <param name="outputStream">The output <see cref="Stream"/> to use as standard output.</param>
    /// <param name="serverName">Optional name of the server, used for diagnostic purposes, like logging.</param>
    /// <param name="loggerFactory">Optional logger factory used for logging employed by the transport.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inputStream"/> or <paramref name="outputStream"/> is <see langword="null"/>.</exception>
    public StreamServerTransport(Stream inputStream, Stream outputStream, string? serverName = null, ILoggerFactory? loggerFactory = null)
        : base(serverName is not null ? $"Server (stream) ({serverName})" : "Server (stream)", loggerFactory)
    {
        Throw.IfNull(inputStream);
        Throw.IfNull(outputStream);

        _logger = loggerFactory?.CreateLogger(GetType()) ?? NullLogger.Instance;

        _inputStream = inputStream;
        _outputStream = outputStream;

        SetConnected();
        _readLoopCompleted = Task.Run(ReadMessagesAsync, _shutdownCts.Token);
    }

    /// <inheritdoc/>
    public override async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return;
        }

        using var _ = await _sendLock.LockAsync(cancellationToken).ConfigureAwait(false);

        string id = "(no id)";
        if (message is JsonRpcMessageWithId messageWithId)
        {
            id = messageWithId.Id.ToString();
        }

        try
        {
            await JsonSerializer.SerializeAsync(_outputStream, message, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage)), cancellationToken).ConfigureAwait(false);
            await _outputStream.WriteAsync(s_newlineBytes, cancellationToken).ConfigureAwait(false);
            await _outputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogTransportSendFailed(Name, id, ex);
            throw new IOException("Failed to send message.", ex);
        }
    }

    private async Task ReadMessagesAsync()
    {
        //CancellationToken shutdownToken = _shutdownCts.Token; // the cts field is not read-only, will be defused
        Exception? error = null;
        try
        {
            LogTransportEnteringReadMessagesLoop(Name);

            byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(16 * 1024);
            try
            {
                using var lineStream = new MemoryStream();

                while (!_shutdownCts.Token.IsCancellationRequested)
                {
                    int bytesRead = await _inputStream.ReadAsync(buffer, 0, buffer.Length, _shutdownCts.Token).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        LogTransportEndOfStream(Name);
                        break;
                    }

                    int offset = 0;
                    while (offset < bytesRead)
                    {
                        int newlineIndex = Array.IndexOf(buffer, (byte)'\n', offset, bytesRead - offset);
                        if (newlineIndex < 0)
                        {
                            lineStream.Write(buffer, offset, bytesRead - offset);
                            break;
                        }

                        int partLength = newlineIndex - offset;
                        if (partLength > 0)
                        {
                            lineStream.Write(buffer, offset, partLength);
                        }

                        offset = newlineIndex + 1;

                        if (!lineStream.TryGetBuffer(out ArraySegment<byte> segment))
                        {
                            throw new InvalidOperationException("Expected MemoryStream to expose its buffer.");
                        }

                        ReadOnlySpan<byte> lineBytes = new(segment.Array!, segment.Offset, (int)lineStream.Length);

                        if (!lineBytes.IsEmpty && lineBytes[^1] == (byte)'\r')
                        {
                            lineBytes = lineBytes[..^1];
                        }

                        lineStream.SetLength(0);

                        if (McpTextUtilities.IsWhiteSpace(lineBytes))
                        {
                            continue;
                        }

                        string? lineForLogs = null;
                        if (Logger.IsEnabled(LogLevel.Trace))
                        {
lineForLogs = McpTextUtilities.GetStringFromUtf8(lineBytes);
                        }
                        if (lineForLogs is not null)
                        {
                            LogTransportReceivedMessageSensitive(Name, lineForLogs);
                        }

                        try
                        {
                            var reader = new Utf8JsonReader(lineBytes);
                            if (JsonSerializer.Deserialize(ref reader, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage))) is JsonRpcMessage message)
                            {
                                await WriteMessageAsync(message, _shutdownCts.Token).ConfigureAwait(false);
                            }
                            else if (lineForLogs is not null)
                            {
                                LogTransportMessageParseUnexpectedTypeSensitive(Name, lineForLogs);
                            }
                        }
                        catch (JsonException ex)
                        {
                            if (Logger.IsEnabled(LogLevel.Trace) && lineForLogs is not null)
                            {
                                LogTransportMessageParseFailedSensitive(Name, lineForLogs, ex);
                            }
                            else
                            {
                                LogTransportMessageParseFailed(Name, ex);
                            }

                            // Continue reading even if we fail to parse a message
                        }
                    }
                }
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (OperationCanceledException)
        {
            LogTransportReadMessagesCancelled(Name);
        }
        catch (Exception ex)
        {
            LogTransportReadMessagesFailed(Name, ex);
            error = ex;
        }
        finally
        {
            SetDisconnected(error);
        }
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            LogTransportShuttingDown(Name);

            // Signal to the stdin reading loop to stop.
            await _shutdownCts.CancelAsync().ConfigureAwait(false);
            CanceledTokenSource.Defuse(ref _shutdownCts, dispose: true);

            // Dispose of stdin/out. Cancellation may not be able to wake up operations
            // synchronously blocked in a syscall; we need to forcefully close the handle / file descriptor.
            _inputStream?.Dispose();
            _outputStream?.Dispose();

            // Make sure the work has quiesced.
            try
            {
                await _readLoopCompleted.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogTransportCleanupReadTaskFailed(Name, ex);
            }
        }
        finally
        {
            SetDisconnected();
            LogTransportShutDown(Name);
        }

        GC.SuppressFinalize(this);
    }
}
