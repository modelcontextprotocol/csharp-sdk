using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    private readonly TextReader _inputReader;
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

#if NET
        _inputReader = new StreamReader(inputStream, Encoding.UTF8);
#else
        _inputReader = new CancellableStreamReader(inputStream, Encoding.UTF8);
#endif
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
        try
        {
            LogTransportEnteringReadMessagesLoop(Name);

            while (!shutdownToken.IsCancellationRequested)
            {
                var line = await _inputReader.ReadLineAsync(shutdownToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (line is null)
                    {
                        LogTransportEndOfStream(Name);
                        break;
                    }

                    continue;
                }

                LogTransportReceivedMessageSensitive(Name, line);

                try
                {
                    if (JsonSerializer.Deserialize(line, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage))) is JsonRpcMessage message)
                    {
                        await WriteMessageAsync(message, shutdownToken).ConfigureAwait(false);
                    }
                    else
                    {
                        LogTransportMessageParseUnexpectedTypeSensitive(Name, line);
                    }
                }
                catch (JsonException ex)
                {
                    if (Logger.IsEnabled(LogLevel.Trace))
                    {
                        LogTransportMessageParseFailedSensitive(Name, line, ex);
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
                        var errorResponse = new JsonRpcError
                        {
                            Id = id,
                            Error = new JsonRpcErrorDetail
                            {
                                Code = (int)McpErrorCode.ParseError,
                                Message = "Failed to parse the JSON-RPC request.",
                            },
                        };

                        try
                        {
                            await SendMessageAsync(errorResponse, shutdownToken).ConfigureAwait(false);
                        }
                        catch (Exception sendEx) when (sendEx is not OperationCanceledException)
                        {
                            // Swallow so a failed error-send does not tear down the read loop. No logging
                            // here because SendMessageAsync already logs send failures before it throws.
                        }
                    }
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

    /// <summary>
    /// Attempts to recover the JSON-RPC request id from a line that failed full deserialization.
    /// </summary>
    /// <remarks>
    /// This walks only the top-level object looking for an "id" property and skips every other value,
    /// using a large reader depth so a deeply nested "params" value cannot make recovery itself fail.
    /// </remarks>
    private static bool TryRecoverRequestId(string line, out RequestId id)
    {
        id = default;

        try
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(line);
            var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
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
            _inputReader?.Dispose();
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
