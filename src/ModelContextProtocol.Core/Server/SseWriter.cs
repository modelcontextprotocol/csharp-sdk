using ModelContextProtocol.Protocol;
using System.Buffers;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace ModelContextProtocol.Server;

internal sealed class SseWriter(string? messageEndpoint = null, BoundedChannelOptions? channelOptions = null) : IAsyncDisposable
{
    private readonly Channel<SseItem<JsonRpcMessage?>> _messages = Channel.CreateBounded<SseItem<JsonRpcMessage?>>(channelOptions ?? new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
    });

    private Utf8JsonWriter? _jsonWriter;
    private Task? _writeTask;
    private CancellationToken? _writeCancellationToken;

    private readonly SemaphoreSlim _disposeLock = new(1, 1);
    private bool _disposed;

    public Task WriteAllAsync(Stream sseResponseStream, CancellationToken cancellationToken)
    {
        Throw.IfNull(sseResponseStream);

        // When messageEndpoint is set, the very first SSE event isn't really an IJsonRpcMessage, but there's no API to write a single
        // item of a different type, so we fib and special-case the "endpoint" event type in the formatter.
        if (messageEndpoint is not null && !_messages.Writer.TryWrite(new SseItem<JsonRpcMessage?>(null, "endpoint")))
        {
            throw new InvalidOperationException("You must call RunAsync before calling SendMessageAsync.");
        }

        _writeCancellationToken = cancellationToken;

        var messages = _messages.Reader.ReadAllAsync(cancellationToken);
        _writeTask = SseFormatter.WriteAsync(messages, sseResponseStream, WriteJsonRpcMessageToBuffer, cancellationToken);
        return _writeTask;
    }

    public Task<bool> SendPrimingEventAsync(TimeSpan retryInterval, ISseEventStreamWriter eventStreamWriter, CancellationToken cancellationToken = default)
    {
        // Create a priming event: empty data with an event ID
        var primingItem = new SseItem<JsonRpcMessage?>(null, "prime")
        {
            ReconnectionInterval = retryInterval,
        };

        return SendMessageAsync(primingItem, eventStreamWriter, cancellationToken);
    }

    public Task<bool> SendMessageAsync(JsonRpcMessage message, ISseEventStreamWriter? eventStreamWriter, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(message);

        // Emit redundant "event: message" lines for better compatibility with other SDKs.
        return SendMessageAsync(new SseItem<JsonRpcMessage?>(message, SseParser.EventTypeDefault), eventStreamWriter, cancellationToken);
    }

    private async Task<bool> SendMessageAsync(SseItem<JsonRpcMessage?> item, ISseEventStreamWriter? eventStreamWriter, CancellationToken cancellationToken = default)
    {
        using var _ = await _disposeLock.LockAsync(cancellationToken).ConfigureAwait(false);

        if (eventStreamWriter is not null && item.EventId is null)
        {
            // Store the event first, even if the underlying writer has completed, so that
            // messages can still be retrieved from the event store.
            item = await eventStreamWriter.WriteEventAsync(item, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        if (_disposed)
        {
            // Don't throw ObjectDisposedException here; just return false to indicate the message wasn't sent.
            // The calling transport can determine what to do in this case (drop the message, or fall back to another transport).
            return false;
        }

        await _messages.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        using var _ = await _disposeLock.LockAsync().ConfigureAwait(false);

        if (_disposed)
        {
            return;
        }

        _messages.Writer.Complete();
        try
        {
            if (_writeTask is not null)
            {
                await _writeTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_writeCancellationToken?.IsCancellationRequested == true)
        {
            // Ignore exceptions caused by intentional cancellation during shutdown.
        }
        finally
        {
            _jsonWriter?.Dispose();
            _disposed = true;
        }
    }

    private void WriteJsonRpcMessageToBuffer(SseItem<JsonRpcMessage?> item, IBufferWriter<byte> writer)
    {
        if (item.EventType == "endpoint" && messageEndpoint is not null)
        {
            writer.Write(Encoding.UTF8.GetBytes(messageEndpoint));
            return;
        }

        if (item.Data is not null)
        {
            JsonSerializer.Serialize(GetUtf8JsonWriter(writer), item.Data, McpJsonUtilities.JsonContext.Default.JsonRpcMessage!);
        }
    }

    private Utf8JsonWriter GetUtf8JsonWriter(IBufferWriter<byte> writer)
    {
        if (_jsonWriter is null)
        {
            _jsonWriter = new Utf8JsonWriter(writer);
        }
        else
        {
            _jsonWriter.Reset(writer);
        }

        return _jsonWriter;
    }
}

