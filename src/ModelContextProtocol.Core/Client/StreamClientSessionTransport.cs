using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Client;

/// <summary>Provides the client side of a stream-based session transport.</summary>
internal class StreamClientSessionTransport : TransportBase
{
    private const int MaxShutdownDrainBytes = 8 * 1024 * 1024;
    private const int MaxShutdownDrainMessages = 4096;
    private const int MaxShutdownMessageLength = 1024 * 1024;
    private static readonly byte[] s_newlineBytes = "\n"u8.ToArray();

    internal static UTF8Encoding NoBomUtf8Encoding { get; } = new(encoderShouldEmitUTF8Identifier: false);

    private readonly CancellableStreamReader _serverOutput;
    private readonly Stream _serverInputStream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();
    private Task? _readTask;
    private volatile bool _drainIncomingMessages;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamClientSessionTransport"/> class.
    /// </summary>
    /// <param name="serverInput">
    /// The server's input stream. Messages written to this stream will be sent to the server.
    /// </param>
    /// <param name="serverOutput">
    /// The server's output stream. Messages read from this stream will be received from the server.
    /// </param>
    /// <param name="encoding">
    /// The encoding used for reading and writing messages from the input and output streams. Defaults to UTF-8 without BOM if null.
    /// </param>
    /// <param name="endpointName">
    /// A name that identifies this transport endpoint in logs.
    /// </param>
    /// <param name="loggerFactory">
    /// Optional factory for creating loggers. If null, a NullLogger is used.
    /// </param>
    /// <param name="startReading">Whether to start reading immediately rather than after derived initialization.</param>
    /// <remarks>
    /// By default, this constructor starts a background task to read messages from the server output stream.
    /// The transport will be marked as connected once initialized.
    /// </remarks>
    public StreamClientSessionTransport(Stream serverInput, Stream serverOutput, Encoding? encoding, string endpointName, ILoggerFactory? loggerFactory, bool startReading = true)
        : base(endpointName, loggerFactory)
    {
        Throw.IfNull(serverInput);
        Throw.IfNull(serverOutput);

        _serverInputStream = serverInput;
        _serverOutput = new CancellableStreamReader(serverOutput, encoding ?? NoBomUtf8Encoding);

        SetConnected();

        if (startReading)
        {
            StartReading();
        }
    }

    protected Task? ReadMessagesTask => Volatile.Read(ref _readTask);

    /// <summary>Starts reading after state used by overridden cleanup methods has been initialized.</summary>
    protected void StartReading()
    {
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

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await CleanupAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);

        // Ensure the channel is always completed after disposal, even if CleanupAsync
        // returned early because another caller (e.g. ReadMessagesAsync) was already
        // running cleanup. SetDisconnected is idempotent—if the channel was already
        // completed by the other cleanup path, this is a no-op.
        SetDisconnected();
    }

    private async Task ReadMessagesAsync(CancellationToken cancellationToken)
    {
        Exception? error = null;
        try
        {
            LogTransportEnteringReadMessagesLoop(Name);

            int retainedBytes = 0;
            int retainedMessages = 0;
            bool retainMessages = true;
            bool discardLine = false;
            char[] buffer = new char[4096];
            StringBuilder line = new();

            async ValueTask ProcessShutdownMessageAsync(string message)
            {
                if (!retainMessages || message.Length > MaxShutdownMessageLength)
                {
                    retainMessages = false;
                    return;
                }

                int messageBytes = Encoding.UTF8.GetByteCount(message) + 1;
                if (messageBytes > MaxShutdownDrainBytes - retainedBytes ||
                    retainedMessages >= MaxShutdownDrainMessages)
                {
                    retainMessages = false;
                    return;
                }

                LogTransportReceivedMessageSensitive(Name, message);
                await ProcessMessageAsync(message, cancellationToken).ConfigureAwait(false);
                retainedBytes += messageBytes;
                retainedMessages++;
            }

            async ValueTask ProcessLineAsync()
            {
                if (!discardLine && line.Length != 0 && (!_drainIncomingMessages || retainMessages))
                {
                    string message = line.ToString();
                    if (_drainIncomingMessages)
                    {
                        await ProcessShutdownMessageAsync(message).ConfigureAwait(false);
                    }
                    else if (!string.IsNullOrWhiteSpace(message))
                    {
                        LogTransportReceivedMessageSensitive(Name, message);
                        await ProcessMessageAsync(message, cancellationToken).ConfigureAwait(false);
                    }
                }

                line.Clear();
                discardLine = false;
            }

            int charsRead;
            while ((charsRead = await _serverOutput.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
            {
                for (int i = 0; i < charsRead; i++)
                {
                    char character = buffer[i];
                    if (character is '\r' or '\n')
                    {
                        await ProcessLineAsync().ConfigureAwait(false);
                    }
                    else if (!discardLine && (!_drainIncomingMessages || retainMessages))
                    {
                        if (_drainIncomingMessages && line.Length >= MaxShutdownMessageLength)
                        {
                            line.Clear();
                            discardLine = true;
                            retainMessages = false;
                        }
                        else
                        {
                            line.Append(character);
                        }
                    }
                }
            }
            await ProcessLineAsync().ConfigureAwait(false);
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

    private async Task ProcessMessageAsync(string line, CancellationToken cancellationToken)
    {
        try
        {
            var message = (JsonRpcMessage?)JsonSerializer.Deserialize(line.AsSpan().Trim(), McpJsonUtilities.ProtocolOptions.GetTypeInfo(typeof(JsonRpcMessage)));
            if (message != null)
            {
                await WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);
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
        }
    }

    /// <summary>
    /// Cancels the shutdown token to signal that the transport is shutting down,
    /// without performing any other cleanup.
    /// </summary>
    protected void CancelShutdown()
    {
        _shutdownCts.Cancel();
    }

    /// <summary>Switches the read loop to bounded shutdown draining.</summary>
    protected void BeginShutdownDrain()
    {
        _drainIncomingMessages = true;
    }

    protected virtual async ValueTask CleanupAsync(Exception? error = null, CancellationToken cancellationToken = default)
    {
        LogTransportShuttingDown(Name);

        await _shutdownCts.CancelAsync().ConfigureAwait(false);

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
