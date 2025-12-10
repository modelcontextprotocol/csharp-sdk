using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Defines the contract for storing and replaying SSE events to support resumability.
/// </summary>
/// <remarks>
/// <para>
/// When a client reconnects with a <c>Last-Event-ID</c> header, the server uses the event store
/// to replay events that were sent after the specified event ID. This enables clients to
/// recover from connection drops without losing messages.
/// </para>
/// <para>
/// Events are scoped to streams, where each stream corresponds to either a specific request ID
/// (for POST SSE responses) or a special "standalone" stream ID (for unsolicited GET SSE messages).
/// </para>
/// <para>
/// Implementations should be thread-safe, as events may be stored and replayed concurrently.
/// </para>
/// </remarks>
public interface ISseEventStore
{
    /// <summary>
    /// Stores an event for later retrieval.
    /// </summary>
    /// <param name="sessionId">
    /// The ID of the session, or <c>null</c>.
    /// </param>
    /// <param name="streamId">
    /// The ID of the stream the event belongs to. This is typically the JSON-RPC request ID
    /// for POST SSE responses, or a special identifier for the standalone GET SSE stream.
    /// </param>
    /// <param name="message">
    /// The JSON-RPC message to store, or <see langword="null"/> for priming events.
    /// Priming events establish the event ID without carrying a message payload.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The generated event ID for the stored event.</returns>
    ValueTask<string> StoreEventAsync(string sessionId, string streamId, JsonRpcMessage? message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replays events that occurred after the specified event ID.
    /// </summary>
    /// <param name="lastEventId">The ID of the last event the client received.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// An <see cref="SseReplayResult"/> containing the events to replay if the event ID was found;
    /// <see langword="null"/> if the event ID was not found in the store.
    /// </returns>
    ValueTask<SseReplayResult?> GetEventsAfterAsync(
        string lastEventId,
        CancellationToken cancellationToken = default);
}
