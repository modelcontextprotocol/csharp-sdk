using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace ModelContextProtocol.AspNetCore.Tests.Utils;

/// <summary>
/// In-memory event store for testing resumability.
/// This is a simple implementation intended for testing, not for production use.
/// </summary>
public class InMemoryEventStore : ISseEventStore
{
    private readonly ConcurrentDictionary<string, (string SessionId, string StreamId, JsonRpcMessage? Message)> _events = new();
    private long _eventCounter;

    /// <summary>
    /// Gets the list of stored event IDs in order of storage.
    /// </summary>
    public List<string> StoredEventIds { get; } = [];

    /// <summary>
    /// Gets the count of events that have been stored.
    /// </summary>
    public int StoreEventCallCount => StoredEventIds.Count;

    /// <inheritdoc />
    public ValueTask<string> StoreEventAsync(string sessionId, string streamId, JsonRpcMessage? message, CancellationToken cancellationToken = default)
    {
        var eventId = Interlocked.Increment(ref _eventCounter).ToString();
        _events[eventId] = (sessionId, streamId, message);
        lock (StoredEventIds)
        {
            StoredEventIds.Add(eventId);
        }
        return new ValueTask<string>(eventId);
    }

    /// <inheritdoc />
    public ValueTask<SseReplayResult?> GetEventsAfterAsync(
        string lastEventId,
        CancellationToken cancellationToken = default)
    {
        if (!_events.TryGetValue(lastEventId, out var lastEvent))
        {
            return ValueTask.FromResult<SseReplayResult?>(null);
        }

        var sessionId = lastEvent.SessionId;
        var streamId = lastEvent.StreamId;

        return new ValueTask<SseReplayResult?>(new SseReplayResult
        {
            SessionId = sessionId,
            StreamId = streamId,
            Events = GetEventsAsync(lastEventId, sessionId, streamId, cancellationToken)
        });
    }

    private async IAsyncEnumerable<StoredSseEvent> GetEventsAsync(
        string lastEventId,
        string sessionId,
        string streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var startReplay = false;

        foreach (var kvp in _events.OrderBy(e => long.Parse(e.Key)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (kvp.Key == lastEventId)
            {
                startReplay = true;
                continue;
            }

            if (startReplay && kvp.Value.SessionId == sessionId && kvp.Value.StreamId == streamId && kvp.Value.Message is not null)
            {
                yield return new StoredSseEvent
                {
                    Message = kvp.Value.Message,
                    EventId = kvp.Key
                };
            }
        }

        await Task.CompletedTask;
    }
}
