using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Collections.Concurrent;
using System.Net.ServerSentEvents;

namespace AspNetCoreMcpServer.EventStore;

/// <summary>
/// Represents an in-memory implementation of an event store that stores and replays events associated with specific
/// streams. This class is designed to handle events of type <see cref="SseItem{T}"/> where the data payload is a <see
/// cref="JsonRpcMessage"/>.
/// </summary>
/// <remarks>The <see cref="InMemoryEventStore"/> provides functionality to store events for a given stream and
/// replay events after a specified event ID. It supports resumability for specific types of requests and ensures events
/// are replayed in the correct order.</remarks>
public sealed class InMemoryEventStore : IEventStore, IEventStoreCleaner
{
    public const string EventIdDelimiter = "_";
    private static ConcurrentDictionary<string, List<SseItem<JsonRpcMessage?>>> _eventStore = new();

    private readonly ILogger<InMemoryEventStore> _logger;
    private readonly TimeSpan _eventsRetentionDurationInMinutes;

    public InMemoryEventStore(IConfiguration configuration, ILogger<InMemoryEventStore> logger)
    {
        _eventsRetentionDurationInMinutes = TimeSpan.FromMinutes(configuration.GetValue<int>("EventStore:EventsRetentionDurationInMinutes", 60));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void StoreEvent(string streamId, SseItem<JsonRpcMessage?> messageItem)
    {
        // remove ElicitationCreate method check to support resumability for other type of requests
        if (messageItem.Data is JsonRpcRequest jsonRpcReq && jsonRpcReq.Method == RequestMethods.ElicitationCreate)
        {
            var sseItemList = _eventStore.GetOrAdd(streamId, (key) => new List<SseItem<JsonRpcMessage?>>());
            sseItemList.Add(messageItem);
        }

        if (messageItem.Data is JsonRpcResponse jsonRpcResp && 
            _eventStore.TryGetValue(streamId, out var itemList))
        {
            itemList.Add(messageItem);
        }
    }

    public async Task ReplayEventsAfter(string lastEventId, Action<IAsyncEnumerable<SseItem<JsonRpcMessage>>> sendEvents)
    {
        var streamId = lastEventId.Split(EventIdDelimiter)[0];
        var events = _eventStore.GetValueOrDefault(streamId, new());
        var sortedAndFilteredEventsToSend = events
            .Where(e => e.Data is not null && e.EventId != null)
            .OrderBy(e => e.EventId)
            // Sending events with EventId greater than lastEventId.
            .SkipWhile(e => string.Compare(e.EventId!, lastEventId, StringComparison.Ordinal) <= 0)
            .Select(e =>
                new SseItem<JsonRpcMessage>(e.Data!, e.EventType)
                {
                    EventId = e.EventId,
                    ReconnectionInterval = e.ReconnectionInterval
                });
        sendEvents(SseItemsAsyncEnumerable(sortedAndFilteredEventsToSend));
    }

    private static async IAsyncEnumerable<SseItem<JsonRpcMessage>> SseItemsAsyncEnumerable(IEnumerable<SseItem<JsonRpcMessage>> enumerableItems)
    {
        foreach (var sseItem in enumerableItems)
        {
            yield return sseItem;
        }
    }

    public string? GetEventId(string streamId, JsonRpcMessage message)
    {
        return $"{streamId}{EventIdDelimiter}{DateTime.UtcNow.Ticks}";
    }

    public void CleanEventStore()
    {
        var cutoffTime = DateTime.UtcNow - _eventsRetentionDurationInMinutes;
        _logger.LogInformation("Cleaning up events older than {CutoffTime} from event store.",  cutoffTime);

        foreach (var key in _eventStore.Keys)
        {
            if (_eventStore.TryGetValue(key, out var itemList))
            {
                itemList.RemoveAll(item => item.EventId != null && 
                    long.TryParse(item.EventId.Split(EventIdDelimiter)[1], out var ticks) && 
                    new DateTime(ticks) < cutoffTime);
                if (itemList.Count == 0)
                {
                    _logger.LogInformation("Removing empty event stream with key {EventStreamKey} from event store.", key);
                    _eventStore.TryRemove(key, out _);
                }
            }
        }
    }
}
