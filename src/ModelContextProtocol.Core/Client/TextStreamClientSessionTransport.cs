using Microsoft.Extensions.Logging;
using ModelContextProtocol.Core;
using ModelContextProtocol.Protocol;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Client;

/// <summary>Provides the client side of a text-based session transport.</summary>
internal class TextStreamClientSessionTransport : TransportBase
{
    internal static UTF8Encoding NoBomUtf8Encoding { get; } = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly byte[] NewlineUtf8 = [(byte)'\n'];

    private readonly TextWriter _serverInput;
    private readonly TextReader? _serverOutput;
    private readonly Stream? _serverOutputStream;

    private readonly SemaphoreSlim _sendLock = new(1, 1);

    // Intentionally not disposed; once this transport instance is collectable, CTS finalization will clean up.
    private readonly CancellationTokenSource _shutdownCts = new();

    private Task? _readTask;

    public TextStreamClientSessionTransport(
        TextWriter serverInput, TextReader serverOutput, string endpointName, ILoggerFactory? loggerFactory)
        : base(endpointName, loggerFactory)
    {
        Throw.IfNull(serverInput);
        Throw.IfNull(serverOutput);

        _serverInput = serverInput;

        if (serverOutput is StreamReader sr && sr.CurrentEncoding.CodePage == Encoding.UTF8.CodePage)
        {
            _serverOutput = null;
            _serverOutputStream = sr.BaseStream;
        }
        else
        {
            _serverOutput = serverOutput;
            _serverOutputStream = null;
        }

        SetConnected();
        StartReadLoop();
    }

    public TextStreamClientSessionTransport(Stream serverInput, Stream serverOutput, Encoding? encoding, string endpointName, ILoggerFactory? loggerFactory)
        : base(endpointName, loggerFactory)
    {
        Throw.IfNull(serverInput);
        Throw.IfNull(serverOutput);

        _serverInput = new StreamWriter(serverInput, encoding ?? NoBomUtf8Encoding);
        _serverOutput = null;
        _serverOutputStream = serverOutput;

        SetConnected();
        StartReadLoop();
    }

    private void StartReadLoop()
    {
        // Start reading messages in the background. We use the rarer pattern of new Task + Start
        // in order to ensure that the body of the task will always see _readTask initialized.
        // It is then able to reliably null it out on completion.
        var readTask = new Task<Task>(
            thisRef => ((TextStreamClientSessionTransport)thisRef!).ReadMessagesAsync(_shutdownCts.Token),
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

        using var _ = await _sendLock.LockAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Prefer writing UTF-8 directly to avoid staging JSON in UTF-16.
            if (_serverInput is StreamWriter sw)
            {
                using var jsonWriter = new Utf8JsonWriter(sw.BaseStream);
                JsonSerializer.Serialize(jsonWriter, message, McpJsonUtilities.JsonContext.Default.JsonRpcMessage);
                await jsonWriter.FlushAsync(cancellationToken).ConfigureAwait(false);

                await sw.BaseStream.WriteAsync(NewlineUtf8, cancellationToken).ConfigureAwait(false);
                await sw.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            // Fallback for arbitrary TextWriter instances: avoid allocating a UTF-16 string.
            byte[] utf8JsonBytes = JsonSerializer.SerializeToUtf8Bytes(message, McpJsonUtilities.JsonContext.Default.JsonRpcMessage);

            int charCount = Encoding.UTF8.GetCharCount(utf8JsonBytes);
            char[] rented = ArrayPool<char>.Shared.Rent(charCount);
            try
            {
                int charsWritten = Encoding.UTF8.GetChars(utf8JsonBytes, 0, utf8JsonBytes.Length, rented, 0);
                await _serverInput.WriteAsync(rented, 0, charsWritten).ConfigureAwait(false);
                await _serverInput.WriteAsync('\n').ConfigureAwait(false);
                await _serverInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(rented);
            }
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

            if (_serverOutputStream is not null)
            {
                await ReadMessagesFromStreamAsync(_serverOutputStream, cancellationToken).ConfigureAwait(false);
                return;
            }

            TextReader serverOutput = _serverOutput ?? throw new InvalidOperationException("No output stream configured.");
            while (true)
            {
                if (await serverOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not string line)
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

    private async Task ReadMessagesFromStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            using var lineStream = new MemoryStream();

            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
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

                    ReadOnlyMemory<byte> lineBytes = new(segment.Array!, segment.Offset, (int)lineStream.Length);

                    if (!lineBytes.IsEmpty && lineBytes.Span[^1] == (byte)'\r')
                    {
                        lineBytes = lineBytes[..^1];
                    }

                    lineStream.SetLength(0);

                    if (McpTextUtilities.IsWhiteSpace(lineBytes.Span))
                    {
                        continue;
                    }

                    await ProcessMessageAsync(lineBytes, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task ProcessMessageAsync(ReadOnlyMemory<byte> lineBytes, CancellationToken cancellationToken)
    {
        ReadOnlySpan<byte> span = lineBytes.Span;

        string? lineForLogs = null;
        if (Logger.IsEnabled(LogLevel.Trace))
        {
lineForLogs = McpTextUtilities.GetStringFromUtf8(span);
        }

        if (lineForLogs is not null)
        {
            LogTransportReceivedMessageSensitive(Name, lineForLogs);
        }

        try
        {
            var reader = new Utf8JsonReader(span);
            var message = (JsonRpcMessage?)JsonSerializer.Deserialize(ref reader, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage)));
            if (message != null)
            {
                await WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);
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
