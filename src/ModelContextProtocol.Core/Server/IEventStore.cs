using ModelContextProtocol.Protocol;
using System.Net.ServerSentEvents;

namespace ModelContextProtocol.Server;

/// <summary>
/// Interface for resumability support via event storage
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Stores an event in the specified stream and returns the unique identifier of the stored event.
    /// </summary>
    /// <remarks>This method asynchronously stores the provided event in the specified stream. The returned
    /// event identifier can be used to retrieve or reference the stored event in the future.</remarks>
    /// <param name="streamId">The identifier of the stream where the event will be stored. Cannot be null or empty.</param>
    /// <param name="messageItem"> The event item to be stored, which may contain a JSON-RPC message or be null.</param>
    void storeEvent(string streamId, SseItem<JsonRpcMessage?> messageItem);


    /// <summary>
    /// Replays events that occurred after the specified event ID.
    /// </summary>
    /// <param name="lastEventId">The ID of the last event that was processed. Events occurring after this ID will be replayed.</param>
    /// <param name="sendEvents">A callback action that processes the replayed events as an asynchronous enumerable of <see cref="SseItem{T}"/>
    /// containing <see cref="JsonRpcMessage"/> objects.</param>
    /// <returns>A task that represents the asynchronous operation of replaying events.</returns>
    Task replayEventsAfter(string lastEventId, Action<IAsyncEnumerable<SseItem<JsonRpcMessage>>> sendEvents);
}
