using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Collections.Concurrent;
using System.Net.ServerSentEvents;

namespace ModelContextProtocol.AspNetCore;
internal sealed class InMemoryEventStore : IEventStore
{
    private ConcurrentDictionary<string, List<SseItem<JsonRpcMessage?>>> eventStore = new();

    public void storeEvent(string streamId, SseItem<JsonRpcMessage?> messageItem)
    {
        // remove ElicitationCreate method check to support resumability for other type of requests
        if (messageItem.Data is JsonRpcRequest jsonRpcReq && jsonRpcReq.Method == RequestMethods.ElicitationCreate)
        {
            var sseItemList = eventStore.GetOrAdd(streamId, (key) => new List<SseItem<JsonRpcMessage?>>());
            sseItemList.Add(messageItem);
        }

        if (messageItem.Data is JsonRpcResponse jsonRpcResp && 
            eventStore.TryGetValue(streamId, out var itemList))
        {
            itemList.Add(messageItem);
        }
    }

    public async Task replayEventsAfter(string lastEventId, Action<IAsyncEnumerable<SseItem<JsonRpcMessage>>> sendEvents)
    {
        var streamId = lastEventId.Split('_')[0];
        var events = eventStore.GetValueOrDefault(streamId, new());
        var sortedAndFilteredEventsToSend = events
            .Where(e => e.Data is not null && e.EventId != null)
            .OrderBy(e => e.EventId)
            // sending events with EventId greater than or equal to lastEventId
            .SkipWhile(e => string.Compare(e.EventId!, lastEventId, StringComparison.Ordinal) < 0)
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
}
