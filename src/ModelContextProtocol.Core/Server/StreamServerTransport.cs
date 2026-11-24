using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides an <see cref="ITransport"/> implemented using a pair of input and output streams.
/// </summary>
/// <remarks>
/// The <see cref="StreamServerTransport"/> class implements bidirectional JSON-RPC messaging over arbitrary
/// streams, allowing MCP communication with clients through various I/O channels such as network sockets,
/// memory streams, or pipes.
/// </remarks>
public partial class StreamServerTransport : TransportBase
{
    // Pooled read buffers. The 4 KiB default of StreamPipeReaderOptions makes a large message span too
    // many segments; 64 KiB keeps the segment count low without holding much memory for small messages.
    private const int ReadBufferSize = 64 * 1024;
    private const int MinimumReadSize = 16 * 1024;

    private static readonly byte[] s_newlineBytes = "\n"u8.ToArray();

    private static readonly JsonReaderOptions s_readerOptions = new()
    {
        // The same reader settings JsonSerializer.Deserialize(string, options) would use.
        AllowTrailingCommas = McpJsonUtilities.ProtocolOptions.AllowTrailingCommas,
        CommentHandling = McpJsonUtilities.ProtocolOptions.ReadCommentHandling,
        MaxDepth = McpJsonUtilities.ProtocolOptions.MaxDepth,
    };

    private readonly ILogger _logger;

    private readonly Stream _inputStream;
    private readonly PipeReader _inputReader;
    private readonly Stream _outputStream;
    private readonly long? _maxMessageSize;
    private readonly JsonTypeInfo _messageTypeInfo = McpJsonUtilities.ProtocolOptions.GetTypeInfo(typeof(JsonRpcMessage));

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();

    private readonly Task _readLoopCompleted;
    private int _disposed = 0;

    // State touched only by the read loop.
    private long _searchedLength;
    private bool _atStreamStart = true;
    private bool _discarding;
    private long _discardedLength;
    private JsonRpcFrameScanner? _discardScanner;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamServerTransport"/> class with explicit input/output streams.
    /// </summary>
    /// <param name="inputStream">The input <see cref="Stream"/> to use as standard input.</param>
    /// <param name="outputStream">The output <see cref="Stream"/> to use as standard output.</param>
    /// <param name="serverName">Optional name of the server, used for diagnostic purposes, like logging.</param>
    /// <param name="loggerFactory">Optional logger factory used for logging employed by the transport.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inputStream"/> or <paramref name="outputStream"/> is <see langword="null"/>.</exception>
    public StreamServerTransport(Stream inputStream, Stream outputStream, string? serverName = null, ILoggerFactory? loggerFactory = null)
        : this(inputStream, outputStream, serverName, loggerFactory, transportOptions: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamServerTransport"/> class with explicit input/output streams
    /// and transport options.
    /// </summary>
    /// <param name="inputStream">The input <see cref="Stream"/> to use as standard input.</param>
    /// <param name="outputStream">The output <see cref="Stream"/> to use as standard output.</param>
    /// <param name="serverName">Optional name of the server, used for diagnostic purposes, like logging.</param>
    /// <param name="loggerFactory">Optional logger factory used for logging employed by the transport.</param>
    /// <param name="transportOptions">Optional transport options, such as <see cref="StreamServerTransportOptions.MaxMessageSize"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inputStream"/> or <paramref name="outputStream"/> is <see langword="null"/>.</exception>
    public StreamServerTransport(
        Stream inputStream,
        Stream outputStream,
        string? serverName,
        ILoggerFactory? loggerFactory,
        StreamServerTransportOptions? transportOptions)
        : base(serverName is not null ? $"Server (stream) ({serverName})" : "Server (stream)", loggerFactory)
    {
        Throw.IfNull(inputStream);
        Throw.IfNull(outputStream);

        _logger = loggerFactory?.CreateLogger(GetType()) ?? NullLogger.Instance;

        _inputStream = inputStream;
        _inputReader = PipeReader.Create(inputStream, new StreamPipeReaderOptions(
            bufferSize: ReadBufferSize,
            minimumReadSize: MinimumReadSize,
            leaveOpen: true));
        _outputStream = outputStream;
        _maxMessageSize = transportOptions?.MaxMessageSize;

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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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

        // Messages produced from one read, in input order. Each is either handed to the session or, for an
        // error reply to a request that could not be read, sent back to the peer.
        List<(JsonRpcMessage Message, bool SendToPeer)> messages = [];
        try
        {
            LogTransportEnteringReadMessagesLoop(Name);

            while (!shutdownToken.IsCancellationRequested)
            {
                ReadResult result = await _inputReader.ReadAsync(shutdownToken).ConfigureAwait(false);
                if (result.IsCanceled)
                {
                    break;
                }

                ReadOnlySequence<byte> buffer = result.Buffer;
                SequencePosition consumed = ProcessBuffer(buffer, result.IsCompleted, messages);
                _inputReader.AdvanceTo(consumed, buffer.End);

                // Every message was deserialized into its own objects, so none of them refers to the pipe's
                // buffers any more and they can be dispatched after AdvanceTo.
                foreach ((JsonRpcMessage message, bool sendToPeer) in messages)
                {
                    if (sendToPeer)
                    {
                        await TrySendErrorAsync(message, shutdownToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await WriteMessageAsync(message, shutdownToken).ConfigureAwait(false);
                    }
                }

                messages.Clear();

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
            try
            {
                await _inputReader.CompleteAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogTransportCleanupReadTaskFailed(Name, ex);
            }

            SetDisconnected(error);
        }
    }

    /// <summary>
    /// Splits the buffered input into newline-delimited messages and processes each complete one.
    /// </summary>
    /// <returns>The position up to which the buffer was consumed.</returns>
    /// <remarks>
    /// Bytes of an incomplete line that were already searched for a newline are not searched again on
    /// the next read. A line that grows past the maximum message size is not kept: its bytes are fed to a
    /// <see cref="JsonRpcFrameScanner"/> and released until the newline that ends it arrives.
    /// </remarks>
    private SequencePosition ProcessBuffer(
        ReadOnlySequence<byte> buffer,
        bool isCompleted,
        List<(JsonRpcMessage Message, bool SendToPeer)> messages)
    {
        while (buffer.Slice(_searchedLength).PositionOf((byte)'\n') is SequencePosition newline)
        {
            ReadOnlySequence<byte> line = buffer.Slice(0, newline);
            buffer = buffer.Slice(buffer.GetPosition(1, newline));
            _searchedLength = 0;

            if (_discarding)
            {
                // The end of an oversized message whose start has already been released.
                ContinueDiscarding(line);
                EndDiscarding(messages);
            }
            else if (line.Length > _maxMessageSize)
            {
                // The whole oversized message arrived in this read.
                StartDiscarding(line);
                EndDiscarding(messages);
            }
            else
            {
                ProcessLine(line, messages);
            }
        }

        // What remains is an incomplete line.
        if (_discarding)
        {
            ContinueDiscarding(buffer);
            if (isCompleted)
            {
                EndDiscarding(messages);
            }

            return buffer.End;
        }

        if (buffer.Length > _maxMessageSize)
        {
            StartDiscarding(buffer);
            if (isCompleted)
            {
                EndDiscarding(messages);
            }

            return buffer.End;
        }

        if (isCompleted)
        {
            // A final line without a trailing newline is still a message, as with TextReader.ReadLine.
            if (!buffer.IsEmpty)
            {
                ProcessLine(buffer, messages);
            }

            return buffer.End;
        }

        _searchedLength = buffer.Length;
        return buffer.Start;
    }

    private void ProcessLine(ReadOnlySequence<byte> line, List<(JsonRpcMessage Message, bool SendToPeer)> messages)
    {
        if (_atStreamStart)
        {
            // Skip a UTF-8 byte order mark at the start of the stream, as StreamReader did.
            _atStreamStart = false;
            if (StartsWithUtf8Bom(line))
            {
                line = line.Slice(3);
            }
        }

        if (IsWhiteSpace(line))
        {
            return;
        }

        if (Logger.IsEnabled(LogLevel.Trace))
        {
            LogTransportReceivedMessageSensitive(Name, GetUtf8String(line));
        }

        try
        {
            var reader = new Utf8JsonReader(line, s_readerOptions);
            if (JsonSerializer.Deserialize(ref reader, _messageTypeInfo) is JsonRpcMessage message)
            {
                // Deserialize(string) rejects anything after the value; keep that behavior.
                if (reader.Read())
                {
                    throw new JsonException("The JSON-RPC message is followed by unexpected data.");
                }

                messages.Add((message, false));
            }
            else if (Logger.IsEnabled(LogLevel.Trace))
            {
                LogTransportMessageParseUnexpectedTypeSensitive(Name, GetUtf8String(line));
            }
        }
        catch (JsonException ex)
        {
            if (Logger.IsEnabled(LogLevel.Trace))
            {
                LogTransportMessageParseFailedSensitive(Name, GetUtf8String(line), ex);
            }
            else
            {
                LogTransportMessageParseFailed(Name, ex);
            }

            // Deserializing the full message failed, for example because the params object was nested
            // more deeply than the JSON reader's MaxDepth allows. If the message still carried a request
            // id, reply with a JSON-RPC parse error using that id so the caller's pending request
            // completes instead of hanging until it times out. If no id can be recovered, the message
            // was either a notification or too malformed to correlate, so we just continue reading.
            if (TryRecoverRequestId(line, out RequestId id))
            {
                messages.Add((CreateError(id, McpErrorCode.ParseError, "Failed to parse the JSON-RPC request."), true));
            }
        }
    }

    private void StartDiscarding(in ReadOnlySequence<byte> head)
    {
        // Nothing of the line is kept in the pipe from here on, so there is nothing already searched.
        _searchedLength = 0;
        _atStreamStart = false;
        _discarding = true;
        _discardedLength = 0;
        _discardScanner ??= new JsonRpcFrameScanner();
        _discardScanner.Reset();
        ContinueDiscarding(head);
    }

    private void ContinueDiscarding(in ReadOnlySequence<byte> bytes)
    {
        _discardedLength += bytes.Length;
        _discardScanner!.Feed(bytes);
    }

    private void EndDiscarding(List<(JsonRpcMessage Message, bool SendToPeer)> messages)
    {
        _discarding = false;
        LogTransportMessageTooLarge(Name, _discardedLength, _maxMessageSize.GetValueOrDefault());

        JsonRpcFrameScanner scanner = _discardScanner!;
        if (!scanner.TryGetId(out RequestId id))
        {
            // A notification, or a message too malformed to correlate.
            return;
        }

        if (scanner.HasMethod)
        {
            // A request: tell the peer it was rejected, so its pending request completes.
            messages.Add((CreateError(id, McpErrorCode.InvalidRequest,
                $"The message exceeds the maximum message size of {_maxMessageSize} bytes."), true));
        }
        else if (scanner.HasResultOrError)
        {
            // A response to a request this server sent, carrying this server's own request id. Sending
            // an error to the peer would make no sense, so fail the pending request locally instead.
            messages.Add((CreateError(id, McpErrorCode.InternalError,
                $"The response exceeds the maximum message size of {_maxMessageSize} bytes and was discarded."), false));
        }
    }

    private async Task TrySendErrorAsync(JsonRpcMessage errorResponse, CancellationToken cancellationToken)
    {
        try
        {
            await SendMessageAsync(errorResponse, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception sendEx) when (sendEx is not OperationCanceledException)
        {
            // Swallow so a failed error-send does not tear down the read loop. No logging
            // here because SendMessageAsync already logs send failures before it throws.
        }
    }

    private static JsonRpcError CreateError(RequestId id, McpErrorCode code, string message) => new()
    {
        Id = id,
        Error = new JsonRpcErrorDetail
        {
            Code = (int)code,
            Message = message,
        },
    };

    /// <summary>
    /// Attempts to recover the JSON-RPC request id from a line that failed full deserialization.
    /// </summary>
    /// <remarks>
    /// This walks only the top-level object looking for an "id" property and skips every other value,
    /// using a large reader depth so a deeply nested "params" value cannot make recovery itself fail.
    /// </remarks>
    private static bool TryRecoverRequestId(in ReadOnlySequence<byte> line, out RequestId id)
    {
        id = default;

        try
        {
            var reader = new Utf8JsonReader(line, new JsonReaderOptions
            {
                // Use the maximum reader depth so that an over-nested "params" value cannot make id
                // recovery throw for the same reason the original parse did.
                MaxDepth = int.MaxValue,
            });

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                bool isId = reader.ValueTextEquals("id"u8);

                if (!reader.Read())
                {
                    break;
                }

                if (isId)
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.String:
                            id = new RequestId(reader.GetString()!);
                            return true;

                        case JsonTokenType.Number when reader.TryGetInt64(out long longId):
                            id = new RequestId(longId);
                            return true;

                        default:
                            // An id that is neither a string nor an integer cannot be correlated, so
                            // there is no point sending an error response for it.
                            return false;
                    }
                }

                // Skip the value of any property other than id, including a deeply nested params object.
                reader.Skip();
            }
        }
        catch (Exception)
        {
            // Recovery is best effort. Whether the line was too malformed to locate a top-level id or
            // the reader failed for any other reason, swallow it here and return false. Letting an
            // exception escape would propagate out of the read loop and disconnect the transport,
            // turning a single bad message into a full session teardown.
        }

        return false;
    }

    private static bool StartsWithUtf8Bom(in ReadOnlySequence<byte> line)
    {
        if (line.Length < 3)
        {
            return false;
        }

        Span<byte> start = stackalloc byte[3];
        line.Slice(0, 3).CopyTo(start);
        return start.SequenceEqual("﻿"u8);
    }

    private static bool IsWhiteSpace(in ReadOnlySequence<byte> line)
    {
        foreach (ReadOnlyMemory<byte> segment in line)
        {
            foreach (byte b in segment.Span)
            {
                if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static string GetUtf8String(in ReadOnlySequence<byte> bytes) =>
#if NET
        Encoding.UTF8.GetString(bytes);
#else
        Encoding.UTF8.GetString(bytes.ToArray());
#endif

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
            // The read loop completes the PipeReader itself, since a PipeReader must not be completed
            // while a read on it may still be in progress.
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "{EndpointName} transport discarded a message of {MessageSize} bytes, which exceeds the maximum message size of {MaxMessageSize} bytes.")]
    private partial void LogTransportMessageTooLarge(string endpointName, long messageSize, long maxMessageSize);
}
