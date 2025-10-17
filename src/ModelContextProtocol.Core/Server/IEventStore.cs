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
    void StoreEvent(string streamId, SseItem<JsonRpcMessage?> messageItem);


    /// <summary>
    /// Replays events that occurred after the specified event ID.
    /// </summary>
    /// <param name="lastEventId">The ID of the last event that was processed. Events occurring after this ID will be replayed.</param>
    /// <param name="sendEvents">A callback action that processes the replayed events as an asynchronous enumerable of <see cref="SseItem{T}"/>
    /// containing <see cref="JsonRpcMessage"/> objects.</param>
    /// <returns>A task that represents the asynchronous operation of replaying events.</returns>
    Task ReplayEventsAfter(string lastEventId, Action<IAsyncEnumerable<SseItem<JsonRpcMessage>>> sendEvents);

    /// <summary>
    /// Retrieves the event identifier associated with a specific JSON-RPC message in the given stream.
    /// </summary>
    /// <param name="streamId">The unique identifier of the stream containing the message.</param>
    /// <param name="message">The JSON-RPC message for which the event identifier is to be retrieved.</param>
    /// <returns>The event identifier as a string, or <see langword="null"/> if no event identifier is associated with the
    /// message.</returns>
    string? GetEventId(string streamId, JsonRpcMessage message);

    /// <summary>
    /// Cleans up the event store by removing outdated or unnecessary events.
    /// </summary>
    /// <remarks>This method is typically used to maintain the event store's size and performance by clearing
    /// events that are no longer needed.</remarks>
    void CleanupEventStore();
}
