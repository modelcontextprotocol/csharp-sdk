using ModelContextProtocol.Protocol;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
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
    private bool _completed;
    private bool _disposed;

    /// <summary>
    /// Gets or sets the event store for resumability support.
    /// When set, events are stored before being written and include event IDs.
    /// </summary>
    public IEventStore? EventStore { get; set; }

    /// <summary>
    /// Gets or sets the stream ID for event storage.
    /// This is typically the JSON-RPC request ID or a special identifier for standalone streams.
    /// </summary>
    public string? StreamId { get; set; }

    /// <summary>
    /// Gets or sets the retry interval to suggest to clients in SSE retry field.
    /// When set, the server will include a retry field in priming events.
    /// </summary>
    public TimeSpan? RetryInterval { get; set; }

    /// <summary>
    /// Gets a value indicating whether resumability is enabled for this writer.
    /// Resumability requires both an event store and a stream ID to be set.
    /// </summary>
    [MemberNotNullWhen(true, nameof(EventStore), nameof(StreamId))]
    private bool IsResumabilityEnabled => EventStore is not null && StreamId is not null;

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

        // If resumability is enabled, wrap messages to store events and add IDs
        if (IsResumabilityEnabled)
        {
            messages = StoreAndAddEventIds(messages, cancellationToken);
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
        if (!IsResumabilityEnabled)
        {
            return null;
        }

        using var _ = await _disposeLock.LockAsync(cancellationToken).ConfigureAwait(false);

        if (_completed)
        {
            return null;
        }

        // Store a null message to get an event ID for the priming event
        var eventId = await EventStore.StoreEventAsync(StreamId, null, cancellationToken).ConfigureAwait(false);

        // Create a priming event: empty data with an event ID
        // We use a special "priming" event type that the formatter will handle
        var primingItem = new SseItem<JsonRpcMessage?>(null, "priming") { EventId = eventId };
        if (RetryInterval.HasValue)
        {
            primingItem = primingItem with { ReconnectionInterval = RetryInterval.Value };
        }

        await _messages.Writer.WriteAsync(primingItem, cancellationToken).ConfigureAwait(false);
        return eventId;
    }

    public async Task<bool> SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
        => await SendMessageAsync(message, eventId: null, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Sends a message with an optional pre-assigned event ID.
    /// This is used for replaying stored events with their original IDs.
    /// </summary>
    public async Task<bool> SendMessageAsync(JsonRpcMessage message, string? eventId, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(message);

        using var _ = await _disposeLock.LockAsync(cancellationToken).ConfigureAwait(false);

        if (_completed)
        {
            // Don't throw here; just return false to indicate the message wasn't sent.
            // The calling transport can determine what to do in this case (drop the message, or fall back to another transport).
            return false;
        }

        // Emit redundant "event: message" lines for better compatibility with other SDKs.
        var item = new SseItem<JsonRpcMessage?>(message, SseParser.EventTypeDefault);
        if (eventId is not null)
        {
            item = item with { EventId = eventId };
        }
        await _messages.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Gracefully closes the SSE stream without waiting for remaining messages.
    /// This signals to the client that it should reconnect to receive remaining messages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This implements the SSE polling pattern from SEP-1699: the server can close the connection
    /// after sending a priming event with an event ID. The client will reconnect with the Last-Event-ID
    /// header, and the server will replay any events that were sent after that ID.
    /// </para>
    /// <para>
    /// After calling this method, any subsequent calls to <see cref="SendMessageAsync(JsonRpcMessage, CancellationToken)"/> or
    /// <see cref="SendPrimingEventAsync"/> will return without sending messages.
    /// </para>
    /// </remarks>
    public void Complete()
    {
        _completed = true;
        _messages.Writer.TryComplete();
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

    private async IAsyncEnumerable<SseItem<JsonRpcMessage?>> StoreAndAddEventIds(
        IAsyncEnumerable<SseItem<JsonRpcMessage?>> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Debug.Assert(IsResumabilityEnabled);

        await foreach (var item in messages.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            // Skip endpoint events, priming events, and replayed events (which already have IDs)
            if (item.EventType == "endpoint" || item.EventType == "priming" || item.EventId is not null)
            {
                yield return item;
                continue;
            }

            // Store the event and get an ID
            // Note: EventStore and StreamId are guaranteed non-null because this method is only
            // called from WriteAllAsync when IsResumabilityEnabled is true.
            var eventId = await EventStore!.StoreEventAsync(StreamId!, item.Data, cancellationToken).ConfigureAwait(false);

            // Yield the item with the event ID
            yield return item with { EventId = eventId };
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
