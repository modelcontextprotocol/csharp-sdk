using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System.Buffers;
using System.IO.Pipelines;
using System.Text.Json;

namespace ModelContextProtocol.Client;

/// <summary>Provides the client side of a stream-based session transport.</summary>
internal class StreamClientSessionTransport : TransportBase
{
    private static readonly byte[] s_newlineBytes = "\n"u8.ToArray();

    private readonly PipeReader _serverOutputPipe;
    private readonly Stream _serverInputStream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource? _shutdownCts = new();
    private Task? _readTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamClientSessionTransport"/> class.
    /// </summary>
    /// <param name="serverInput">
    /// The server's input stream. Messages written to this stream will be sent to the server.
    /// </param>
    /// <param name="serverOutput">
    /// The server's output stream. Messages read from this stream will be received from the server.
    /// </param>
    /// <param name="endpointName">
    /// A name that identifies this transport endpoint in logs.
    /// </param>
    /// <param name="loggerFactory">
    /// Optional factory for creating loggers. If null, a NullLogger is used.
    /// </param>
    /// <remarks>
    /// This constructor starts a background task to read messages from the server output stream.
    /// The transport will be marked as connected once initialized.
    /// </remarks>
    public StreamClientSessionTransport(Stream serverInput, Stream serverOutput, string endpointName, ILoggerFactory? loggerFactory)
        : base(endpointName, loggerFactory)
    {
        Throw.IfNull(serverInput);
        Throw.IfNull(serverOutput);

        _serverInputStream = serverInput;
        _serverOutputPipe = PipeReader.Create(serverOutput);

        SetConnected();

        // Start reading messages in the background. We use the rarer pattern of new Task + Start
        // in order to ensure that the body of the task will always see _readTask initialized.
        // It is then able to reliably null it out on completion.
        var readTask = new Task<Task>(
            thisRef => ((StreamClientSessionTransport)thisRef!).ReadMessagesAsync(_shutdownCts.Token),
            this,
            TaskCreationOptions.DenyChildAttach);
        _readTask = readTask.Unwrap();
        readTask.Start();
    }

    /// <inheritdoc/>
    public override async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        string id = "(no id)";
        if (message is JsonRpcMessageWithId messageWithId)
        {
            id = messageWithId.Id.ToString();
        }

        LogTransportSendingMessageSensitive(message);

        using var _ = await _sendLock.LockAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(message, McpJsonUtilities.JsonContext.Default.JsonRpcMessage);
            await _serverInputStream.WriteAsync(json, cancellationToken).ConfigureAwait(false);
            await _serverInputStream.WriteAsync(s_newlineBytes, cancellationToken).ConfigureAwait(false);
            await _serverInputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogTransportSendFailed(Name, id, ex);
            throw new IOException("Failed to send message.", ex);
        }
    }

    /// <inheritdoc/>
    public override ValueTask DisposeAsync() =>
        CleanupAsync(cancellationToken: CancellationToken.None);

    private async Task ReadMessagesAsync(CancellationToken cancellationToken)
    {
        Exception? error = null;
        try
        {
            LogTransportEnteringReadMessagesLoop(Name);
            await _serverOutputPipe.ReadLinesAsync(ProcessLineAsync, cancellationToken).ConfigureAwait(false);
            LogTransportEndOfStream(Name);
        }
        catch (OperationCanceledException)
        {
            LogTransportReadMessagesCancelled(Name);
        }
        catch (Exception ex)
        {
            error = ex;
            LogTransportReadMessagesFailed(Name, ex);
        }
        finally
        {
            _readTask = null;
            await CleanupAsync(error, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessLineAsync(ReadOnlySequence<byte> line, CancellationToken cancellationToken)
    {
        if (Logger.IsEnabled(LogLevel.Trace))
        {
            LogTransportReceivedMessageSensitive(Name, EncodingUtilities.GetUtf8String(line));
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
                    LogTransportMessageParseUnexpectedTypeSensitive(Name, EncodingUtilities.GetUtf8String(line));
                }
            }
        }
        catch (JsonException ex)
        {
            if (Logger.IsEnabled(LogLevel.Trace))
            {
                LogTransportMessageParseFailedSensitive(Name, EncodingUtilities.GetUtf8String(line), ex);
            }
            else
            {
                LogTransportMessageParseFailed(Name, ex);
            }
        }
    }

    protected virtual async ValueTask CleanupAsync(Exception? error = null, CancellationToken cancellationToken = default)
    {
        LogTransportShuttingDown(Name);

        if (Interlocked.Exchange(ref _shutdownCts, null) is { } shutdownCts)
        {
            await shutdownCts.CancelAsync().ConfigureAwait(false);
            shutdownCts.Dispose();
        }

        if (Interlocked.Exchange(ref _readTask, null) is Task readTask)
        {
            try
            {
                await readTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogTransportCleanupReadTaskFailed(Name, ex);
            }
        }

        SetDisconnected(error);
        LogTransportShutDown(Name);
    }
}
