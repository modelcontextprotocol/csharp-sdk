using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using System.Buffers;
using System.IO.Pipelines;
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
    private readonly PipeReader _inputPipeReader;
    private readonly Stream _outputStream;

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
    /// <exception cref="ArgumentNullException"><paramref name="inputStream"/> or <paramref name="outputStream"/> is <see langword="null"/>.</exception>
    public StreamServerTransport(Stream inputStream, Stream outputStream, string? serverName = null, ILoggerFactory? loggerFactory = null)
        : base(serverName is not null ? $"Server (stream) ({serverName})" : "Server (stream)", loggerFactory)
    {
        Throw.IfNull(inputStream);
        Throw.IfNull(outputStream);

        _logger = loggerFactory?.CreateLogger(GetType()) ?? NullLogger.Instance;

        _inputStream = inputStream;
        _inputPipeReader = PipeReader.Create(inputStream);
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
            var json = JsonSerializer.SerializeToUtf8Bytes(message, McpJsonUtilities.JsonContext.Default.JsonRpcMessage);
            LogTransportSendingMessageSensitive(message);
            await _outputStream.WriteAsync(json, cancellationToken).ConfigureAwait(false);
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
        CancellationToken shutdownToken = _shutdownCts.Token;
        Exception? error = null;
        try
        {
            LogTransportEnteringReadMessagesLoop(Name);

            while (!shutdownToken.IsCancellationRequested)
            {
                ReadResult result = await _inputPipeReader.ReadAsync(shutdownToken).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                SequencePosition? position;
                while ((position = buffer.PositionOf((byte)'\n')) != null)
                {
                    ReadOnlySequence<byte> line = buffer.Slice(0, position.Value);

                    // Trim trailing \r for Windows-style CRLF line endings.
                    if (EndsWithCarriageReturn(line))
                    {
                        line = line.Slice(0, line.Length - 1);
                    }

                    if (!line.IsEmpty)
                    {
                        await ProcessLineAsync(line, shutdownToken).ConfigureAwait(false);
                    }

                    // Advance past the '\n'.
                    buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
                }

                _inputPipeReader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    LogTransportEndOfStream(Name);
                    break;
                }
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

    private async Task ProcessLineAsync(ReadOnlySequence<byte> line, CancellationToken cancellationToken)
    {
        if (Logger.IsEnabled(LogLevel.Trace))
        {
            LogTransportReceivedMessageSensitive(Name, GetString(line));
        }

        try
        {
            JsonRpcMessage? message;
            if (line.IsSingleSegment)
            {
                message = JsonSerializer.Deserialize(line.First.Span, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage))) as JsonRpcMessage;
            }
            else
            {
                var reader = new Utf8JsonReader(line, isFinalBlock: true, state: default);
                message = JsonSerializer.Deserialize(ref reader, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage))) as JsonRpcMessage;
            }

            if (message is not null)
            {
                await WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (Logger.IsEnabled(LogLevel.Trace))
                {
                    LogTransportMessageParseUnexpectedTypeSensitive(Name, GetString(line));
                }
            }
        }
        catch (JsonException ex)
        {
            if (Logger.IsEnabled(LogLevel.Trace))
            {
                LogTransportMessageParseFailedSensitive(Name, GetString(line), ex);
            }
            else
            {
                LogTransportMessageParseFailed(Name, ex);
            }

            // Continue reading even if we fail to parse a message.
        }
    }

    private static string GetString(in ReadOnlySequence<byte> sequence) =>
        sequence.IsSingleSegment
            ? Encoding.UTF8.GetString(sequence.First.Span)
            : Encoding.UTF8.GetString(sequence.ToArray());

    private static bool EndsWithCarriageReturn(in ReadOnlySequence<byte> sequence)
    {
        if (sequence.IsSingleSegment)
        {
            ReadOnlySpan<byte> span = sequence.First.Span;
            return span.Length > 0 && span[span.Length - 1] == (byte)'\r';
        }

        // Multi-segment: find the last non-empty segment to check its last byte.
        ReadOnlyMemory<byte> last = default;
        foreach (ReadOnlyMemory<byte> segment in sequence)
        {
            if (!segment.IsEmpty)
            {
                last = segment;
            }
        }

        return !last.IsEmpty && last.Span[last.Length - 1] == (byte)'\r';
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
            _shutdownCts.Dispose();

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
