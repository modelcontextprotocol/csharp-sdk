using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents the result of replaying SSE events to a client during resumption.
/// </summary>
/// <remarks>
/// This class is returned by <see cref="ISseEventStore.GetEventsAfterAsync"/> when a client
/// reconnects with a <c>Last-Event-ID</c> header. It contains the stream and session identifiers
/// along with an async enumerable of events to replay.
/// </remarks>
public sealed class SseReplayResult
{
    /// <summary>
    /// Gets the session ID that the events belong to.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Gets the stream ID that the events belong to.
    /// </summary>
    /// <remarks>
    /// This is typically the JSON-RPC request ID for POST SSE responses,
    /// or a special identifier for the standalone GET SSE stream.
    /// </remarks>
    public required string StreamId { get; init; }

    /// <summary>
    /// Gets the async enumerable of events to replay to the client.
    /// </summary>
    public required IAsyncEnumerable<StoredSseEvent> Events { get; init; }
}
