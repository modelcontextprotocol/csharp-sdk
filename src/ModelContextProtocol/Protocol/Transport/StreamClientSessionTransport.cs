using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.Text.Json;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>Provides the client side of a stream-based session transport.</summary>
internal class StreamClientSessionTransport : TransportBase
{
    private readonly TextReader _serverOutput;
    private readonly TextWriter _serverInput;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource? _shutdownCts = new();
    private Task? _readTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamClientSessionTransport"/> class.
    /// </summary>
    public StreamClientSessionTransport(
        TextWriter serverInput, TextReader serverOutput, string endpointName, ILoggerFactory? loggerFactory)
        : base(loggerFactory)
    {
        Logger = (ILogger?)loggerFactory?.CreateLogger<StdioClientTransport>() ?? NullLogger.Instance;
        _serverOutput = serverOutput;
        _serverInput = serverInput;
        EndpointName = endpointName;

        // Start reading messages in the background
        Logger.TransportReadingMessages(endpointName);
        _readTask = Task.Run(() => ReadMessagesAsync(_shutdownCts.Token), CancellationToken.None);

        SetConnected(true);
    }

    protected ILogger Logger { get; private set; }

    protected string EndpointName { get; }

    /// <inheritdoc/>
    public override async Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            Logger.TransportNotConnected(EndpointName);
            throw new McpTransportException("Transport is not connected");
        }

        string id = "(no id)";
        if (message is IJsonRpcMessageWithId messageWithId)
        {
            id = messageWithId.Id.ToString();
        }

        var json = JsonSerializer.Serialize(message, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IJsonRpcMessage)));

        using var _ = await _sendLock.LockAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Logger.TransportSendingMessage(EndpointName, id, json);
            Logger.TransportMessageBytesUtf8(EndpointName, json);

            // Write the message followed by a newline using our UTF-8 writer
            await _serverInput.WriteLineAsync(json).ConfigureAwait(false);
            await _serverInput.FlushAsync(cancellationToken).ConfigureAwait(false);

            Logger.TransportSentMessage(EndpointName, id);
        }
        catch (Exception ex)
        {
            Logger.TransportSendFailed(EndpointName, id, ex);
            throw new McpTransportException("Failed to send message", ex);
        }
    }

    /// <inheritdoc/>
    public override ValueTask DisposeAsync() =>
        CleanupAsync(CancellationToken.None);

    private async Task ReadMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            Logger.TransportEnteringReadMessagesLoop(EndpointName);

            while (!cancellationToken.IsCancellationRequested)
            {
                Logger.TransportWaitingForMessage(EndpointName);
                if (await _serverOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not string line)
                {
                    Logger.TransportEndOfStream(EndpointName);
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                Logger.TransportReceivedMessage(EndpointName, line);
                Logger.TransportMessageBytesUtf8(EndpointName, line);

                await ProcessMessageAsync(line, cancellationToken).ConfigureAwait(false);
            }
            Logger.TransportExitingReadMessagesLoop(EndpointName);
        }
        catch (OperationCanceledException)
        {
            Logger.TransportReadMessagesCancelled(EndpointName);
        }
        catch (Exception ex)
        {
            Logger.TransportReadMessagesFailed(EndpointName, ex);
        }
        finally
        {
            await CleanupAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessMessageAsync(string line, CancellationToken cancellationToken)
    {
        try
        {
            var message = (IJsonRpcMessage?)JsonSerializer.Deserialize(line.AsSpan().Trim(), McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IJsonRpcMessage)));
            if (message != null)
            {
                string messageId = "(no id)";
                if (message is IJsonRpcMessageWithId messageWithId)
                {
                    messageId = messageWithId.Id.ToString();
                }

                Logger.TransportReceivedMessageParsed(EndpointName, messageId);
                await WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);
                Logger.TransportMessageWritten(EndpointName, messageId);
            }
            else
            {
                Logger.TransportMessageParseUnexpectedType(EndpointName, line);
            }
        }
        catch (JsonException ex)
        {
            Logger.TransportMessageParseFailed(EndpointName, line, ex);
        }
    }

    protected virtual async ValueTask CleanupAsync(CancellationToken cancellationToken)
    {
        Logger.TransportCleaningUp(EndpointName);

        if (Interlocked.Exchange(ref _shutdownCts, null) is { } shutdownCts)
        {
            await shutdownCts.CancelAsync().ConfigureAwait(false);
            shutdownCts.Dispose();
        }

        if (Interlocked.Exchange(ref _readTask, null) is Task readTask)
        {
            try
            {
                Logger.TransportWaitingForReadTask(EndpointName);
                await readTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                Logger.TransportReadTaskCleanedUp(EndpointName);
            }
            catch (TimeoutException)
            {
                Logger.TransportCleanupReadTaskTimeout(EndpointName);
            }
            catch (OperationCanceledException)
            {
                Logger.TransportCleanupReadTaskCancelled(EndpointName);
            }
            catch (Exception ex)
            {
                Logger.TransportCleanupReadTaskFailed(EndpointName, ex);
            }
        }

        SetConnected(false);
        Logger.TransportCleanedUp(EndpointName);
    }
}
