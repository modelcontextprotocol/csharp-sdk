using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Collections.Concurrent;

namespace ModelContextProtocol.AspNetCore.Tests.Utils;

/// <summary>
/// In-memory event store for testing resumability.
/// This is a simple implementation intended for testing, not for production use.
/// </summary>
public class InMemoryEventStore : IEventStore
{
    private readonly ConcurrentDictionary<string, (string StreamId, JsonRpcMessage? Message)> _events = new();
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
    public ValueTask<string> StoreEventAsync(string streamId, JsonRpcMessage? message, CancellationToken cancellationToken = default)
    {
        var eventId = Interlocked.Increment(ref _eventCounter).ToString();
        _events[eventId] = (streamId, message);
        lock (StoredEventIds)
        {
            StoredEventIds.Add(eventId);
        }
        return new ValueTask<string>(eventId);
    }

    /// <inheritdoc />
    public async ValueTask<string?> ReplayEventsAfterAsync(
        string lastEventId,
        Func<JsonRpcMessage, string, CancellationToken, ValueTask> sendCallback,
        CancellationToken cancellationToken = default)
    {
        if (!_events.TryGetValue(lastEventId, out var lastEvent))
        {
            return null;
        }

        var streamId = lastEvent.StreamId;
        var startReplay = false;

        foreach (var kvp in _events.OrderBy(e => long.Parse(e.Key)))
        {
            if (kvp.Key == lastEventId)
            {
                startReplay = true;
                continue;
            }

            if (startReplay && kvp.Value.StreamId == streamId && kvp.Value.Message is not null)
            {
                await sendCallback(kvp.Value.Message, kvp.Key, cancellationToken).ConfigureAwait(false);
            }
        }

        return streamId;
    }
}
