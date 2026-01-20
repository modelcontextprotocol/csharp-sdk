using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Collections.Concurrent;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;

namespace ModelContextProtocol.AspNetCore.Tests.Utils;

/// <summary>
/// In-memory event store for testing resumability.
/// This is a simple implementation intended for testing, not for production use.
/// </summary>
public sealed class TestSseEventStreamStore : ISseEventStreamStore
{
    private readonly ConcurrentDictionary<string, StreamState> _streams = new();
    private readonly ConcurrentDictionary<string, (StreamState Stream, long Sequence)> _eventLookup = new();
    private readonly List<string> _storedEventIds = [];
    private readonly List<TimeSpan> _storedReconnectionIntervals = [];
    private readonly object _storedEventIdsLock = new();
    private int _storeEventCallCount;
    private long _globalSequence;

    /// <summary>
    /// Gets the number of times events have been stored.
    /// </summary>
    public int StoreEventCallCount => _storeEventCallCount;

    /// <summary>
    /// Gets the list of stored event IDs in order.
    /// </summary>
    public IReadOnlyList<string> StoredEventIds
    {
        get
        {
            lock (_storedEventIdsLock)
            {
                return [.. _storedEventIds];
            }
        }
    }

    /// <summary>
    /// Gets the list of stored reconnection intervals in order.
    /// </summary>
    public IReadOnlyList<TimeSpan> StoredReconnectionIntervals
    {
        get
        {
            lock (_storedEventIdsLock)
            {
                return [.. _storedReconnectionIntervals];
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<ISseEventStreamWriter> CreateStreamAsync(SseEventStreamOptions options, CancellationToken cancellationToken = default)
    {
        var streamKey = GetStreamKey(options.SessionId, options.StreamId);
        var state = new StreamState(options.SessionId, options.StreamId, options.Mode);
        if (!_streams.TryAdd(streamKey, state))
        {
            throw new InvalidOperationException($"A stream with key '{streamKey}' has already been created.");
        }
        var writer = new InMemoryEventStreamWriter(this, state);
        return new ValueTask<ISseEventStreamWriter>(writer);
    }

    /// <inheritdoc />
    public ValueTask<ISseEventStreamReader?> GetStreamReaderAsync(string lastEventId, CancellationToken cancellationToken = default)
    {
        // Look up the event by its ID to find which stream it belongs to
        if (!_eventLookup.TryGetValue(lastEventId, out var lookup))
        {
            return new ValueTask<ISseEventStreamReader?>((ISseEventStreamReader?)null);
        }

        var reader = new InMemoryEventStreamReader(lookup.Stream, lookup.Sequence);
        return new ValueTask<ISseEventStreamReader?>(reader);
    }

    private string GenerateEventId() => Interlocked.Increment(ref _globalSequence).ToString();

    private void TrackEvent(string eventId, StreamState stream, long sequence, TimeSpan? reconnectionInterval = null)
    {
        _eventLookup[eventId] = (stream, sequence);
        lock (_storedEventIdsLock)
        {
            _storedEventIds.Add(eventId);
            if (reconnectionInterval.HasValue)
            {
                _storedReconnectionIntervals.Add(reconnectionInterval.Value);
            }
        }
        Interlocked.Increment(ref _storeEventCallCount);
    }

    private static string GetStreamKey(string sessionId, string streamId) => $"{sessionId}:{streamId}";

    /// <summary>
    /// Holds the state for a single stream.
    /// </summary>
    private sealed class StreamState
    {
        private readonly List<(SseItem<JsonRpcMessage?> Item, long Sequence)> _events = [];
        private readonly object _lock = new();
        private TaskCompletionSource _newEventSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private long _sequence;

        public StreamState(string sessionId, string streamId, SseEventStreamMode mode)
        {
            SessionId = sessionId;
            StreamId = streamId;
            Mode = mode;
        }

        public string SessionId { get; }
        public string StreamId { get; }
        public SseEventStreamMode Mode { get; set; }
        public bool IsCompleted { get; private set; }

        public long NextSequence() => Interlocked.Increment(ref _sequence);

        public void AddEvent(SseItem<JsonRpcMessage?> item, long sequence)
        {
            lock (_lock)
            {
                if (IsCompleted)
                {
                    throw new InvalidOperationException("Cannot add events to a completed stream.");
                }

                _events.Add((item, sequence));

                var oldSignal = _newEventSignal;
                _newEventSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                oldSignal.TrySetResult();
            }
        }

        public (List<SseItem<JsonRpcMessage?>> Events, long LastSequence, Task NewEventSignal) GetEventsAfter(long sequence)
        {
            lock (_lock)
            {
                var result = new List<SseItem<JsonRpcMessage?>>();
                long lastSequence = sequence;

                foreach (var (item, seq) in _events)
                {
                    if (seq > sequence)
                    {
                        result.Add(item);
                        lastSequence = seq;
                    }
                }

                return (result, lastSequence, _newEventSignal.Task);
            }
        }

        public void Complete()
        {
            lock (_lock)
            {
                IsCompleted = true;
                _newEventSignal.TrySetResult();
            }
        }
    }

    private sealed class InMemoryEventStreamWriter : ISseEventStreamWriter
    {
        private readonly TestSseEventStreamStore _store;
        private readonly StreamState _state;
        private bool _disposed;

        public InMemoryEventStreamWriter(TestSseEventStreamStore store, StreamState state)
        {
            _store = store;
            _state = state;
        }

        public ValueTask SetModeAsync(SseEventStreamMode mode, CancellationToken cancellationToken = default)
        {
            _state.Mode = mode;
            return default;
        }

        public ValueTask<SseItem<JsonRpcMessage?>> WriteEventAsync(SseItem<JsonRpcMessage?> sseItem, CancellationToken cancellationToken = default)
        {
            // Skip if already has an event ID
            if (sseItem.EventId is not null)
            {
                return new ValueTask<SseItem<JsonRpcMessage?>>(sseItem);
            }

            var sequence = _state.NextSequence();
            var eventId = _store.GenerateEventId();
            var newItem = sseItem with { EventId = eventId };

            _state.AddEvent(newItem, sequence);
            _store.TrackEvent(eventId, _state, sequence, sseItem.ReconnectionInterval);

            return new ValueTask<SseItem<JsonRpcMessage?>>(newItem);
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return default;
            }

            _disposed = true;
            _state.Complete();
            return default;
        }
    }

    private sealed class InMemoryEventStreamReader : ISseEventStreamReader
    {
        private readonly StreamState _state;
        private readonly long _startSequence;

        public InMemoryEventStreamReader(StreamState state, long startSequence)
        {
            _state = state;
            _startSequence = startSequence;
        }

        public string SessionId => _state.SessionId;
        public string StreamId => _state.StreamId;

        public async IAsyncEnumerable<SseItem<JsonRpcMessage?>> ReadEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            long lastSeenSequence = _startSequence;

            while (true)
            {
                // Get events after the last seen sequence
                var (events, lastSequence, newEventSignal) = _state.GetEventsAfter(lastSeenSequence);

                foreach (var evt in events)
                {
                    yield return evt;
                }

                // Update to the sequence we actually retrieved
                lastSeenSequence = lastSequence;

                // If in polling mode, stop after returning currently available events
                if (_state.Mode == SseEventStreamMode.Polling)
                {
                    yield break;
                }

                // If the stream is completed, stop
                if (_state.IsCompleted)
                {
                    yield break;
                }

                // Wait for new events or cancellation
                await newEventSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
