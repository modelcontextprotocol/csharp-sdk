using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Client;

/// <summary>Provides the client side of a stream-based session transport.</summary>
internal class StreamClientSessionTransport : TransportBase
{
    internal static UTF8Encoding NoBomUtf8Encoding { get; } = new(encoderShouldEmitUTF8Identifier: false);

    private readonly TextReader _serverOutput;
    private readonly TextWriter _serverInput;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource? _shutdownCts = new();
    private Task? _readTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamClientSessionTransport"/> class.
    /// </summary>
    /// <param name="serverInput">
    /// The text writer connected to the server's input stream.
    /// Messages written to this writer will be sent to the server.
    /// </param>
    /// <param name="serverOutput">
    /// The text reader connected to the server's output stream.
    /// Messages read from this reader will be received from the server.
    /// </param>
    /// <param name="endpointName">
    /// A name that identifies this transport endpoint in logs.
    /// </param>
    /// <param name="loggerFactory">
    /// Optional factory for creating loggers. If null, a NullLogger will be used.
    /// </param>
    /// <remarks>
    /// This constructor starts a background task to read messages from the server output stream.
    /// The transport will be marked as connected once initialized.
    /// </remarks>
    public StreamClientSessionTransport(
        TextWriter serverInput, TextReader serverOutput, string endpointName, ILoggerFactory? loggerFactory)
        : base(endpointName, loggerFactory)
    {
        _serverOutput = serverOutput;
        _serverInput = serverInput;

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
    /// Optional factory for creating loggers. If null, a NullLogger will be used.
    /// </param>
    /// <remarks>
    /// This constructor starts a background task to read messages from the server output stream.
    /// The transport will be marked as connected once initialized.
    /// </remarks>
    public StreamClientSessionTransport(Stream serverInput, Stream serverOutput, Encoding? encoding, string endpointName, ILoggerFactory? loggerFactory)
        : this(
            new StreamWriter(serverInput, encoding ?? NoBomUtf8Encoding),
#if NET
            new StreamReader(serverOutput, encoding ?? NoBomUtf8Encoding),
#else
            new CancellableStreamReader(serverOutput, encoding ?? NoBomUtf8Encoding),
#endif
            endpointName,
            loggerFactory)
    {
        Throw.IfNull(serverInput);
        Throw.IfNull(serverOutput);
    }

    /// <inheritdoc/>
    public override async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        string id = "(no id)";
        if (message is JsonRpcMessageWithId messageWithId)
        {
            id = messageWithId.Id.ToString();
        }

        var json = JsonSerializer.Serialize(message, McpJsonUtilities.JsonContext.Default.JsonRpcMessage);

        using var _ = await _sendLock.LockAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Write the message followed by a newline using our UTF-8 writer
            await _serverInput.WriteLineAsync(json).ConfigureAwait(false);
            await _serverInput.FlushAsync(cancellationToken).ConfigureAwait(false);
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

            while (true)
            {
                if (await _serverOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not string line)
                {
                    LogTransportEndOfStream(Name);
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                LogTransportReceivedMessageSensitive(Name, line);

                await ProcessMessageAsync(line, cancellationToken).ConfigureAwait(false);
            }
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
            var message = (JsonRpcMessage?)JsonSerializer.Deserialize(line.AsSpan().Trim(), McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage)));
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
