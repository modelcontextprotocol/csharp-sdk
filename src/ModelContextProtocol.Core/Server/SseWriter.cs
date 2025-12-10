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

    public SseStreamEventStore? EventStore { get; set; }

    public Func<IAsyncEnumerable<SseItem<JsonRpcMessage?>>, CancellationToken, IAsyncEnumerable<SseItem<JsonRpcMessage?>>>? MessageFilter { get; set; }

    public async Task WriteAllAsync(Stream sseResponseStream, CancellationToken cancellationToken)
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
        if (MessageFilter is not null)
        {
            messages = MessageFilter(messages, cancellationToken);
        }

        _writeTask = SseFormatter.WriteAsync(messages, sseResponseStream, WriteJsonRpcMessageToBuffer, cancellationToken);
        await _writeTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a priming event with an event ID but no message payload.
    /// This establishes resumability for the stream before any actual messages are sent.
    /// </summary>
    public async Task<string?> SendPrimingEventAsync(CancellationToken cancellationToken = default)
    {
        if (EventStore is null)
        {
            return null;
        }

        using var _ = await _disposeLock.LockAsync(cancellationToken).ConfigureAwait(false);

        if (_disposed)
        {
            return null;
        }

        // Store a null message to get an event ID for the priming event
        var eventId = await EventStore.StoreEventAsync(message: null, cancellationToken);

        // Create a priming event: empty data with an event ID
        // We use a special "priming" event type that the formatter will handle
        var primingItem = new SseItem<JsonRpcMessage?>(null, "priming")
        {
            EventId = eventId,
            ReconnectionInterval = EventStore.RetryInterval,
        };

        await _messages.Writer.WriteAsync(primingItem, cancellationToken).ConfigureAwait(false);
        return eventId;
    }

    public async Task<bool> SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
        => await SendMessageAsync(message, eventId: null, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Sends a message with an optional pre-assigned event ID.
    /// This is used for replaying stored events with their original IDs.
    /// </summary>
    /// <remarks>
    /// If resumability is enabled and no event ID is provided, the message is stored
    /// in the event store before being written to the stream. This ensures messages
    /// are persisted even if the stream is closed before they can be written.
    /// </remarks>
    public async Task<bool> SendMessageAsync(JsonRpcMessage message, string? eventId, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(message);

        using var _ = await _disposeLock.LockAsync(cancellationToken).ConfigureAwait(false);

        // Store the event first (even if stream is completed) so clients can retrieve it via Last-Event-ID.
        // Skip if eventId is already provided (replayed events are already stored).
        if (eventId is null && EventStore is not null)
        {
            eventId = await EventStore.StoreEventAsync(message, cancellationToken).ConfigureAwait(false);
        }

        if (_disposed)
        {
            // Message is stored but stream is closed - client can retrieve via Last-Event-ID.
            return false;
        }

        var item = new SseItem<JsonRpcMessage?>(message, SseParser.EventTypeDefault)
        {
            EventId = eventId,
        };
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

        // Signal completion if not already done (e.g., by Complete())
        _messages.Writer.TryComplete();
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

        // Priming events have empty data - just write nothing
        if (item.EventType == "priming")
        {
            return;
        }

        JsonSerializer.Serialize(GetUtf8JsonWriter(writer), item.Data, McpJsonUtilities.JsonContext.Default.JsonRpcMessage!);
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
