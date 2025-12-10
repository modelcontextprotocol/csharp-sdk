using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents a stored SSE event that can be replayed to a client during resumption.
/// </summary>
/// <remarks>
/// This struct is used when replaying events from an <see cref="ISseEventStore"/>
/// after a client reconnects with a <c>Last-Event-ID</c> header.
/// </remarks>
public readonly struct StoredSseEvent
{
    /// <summary>
    /// Gets the JSON-RPC message that was stored for this event.
    /// </summary>
    public required JsonRpcMessage Message { get; init; }

    /// <summary>
    /// Gets the unique event ID that was assigned when the event was stored.
    /// </summary>
    /// <remarks>
    /// This ID is sent to the client so it can be used in subsequent
    /// <c>Last-Event-ID</c> headers for further resumption.
    /// </remarks>
    public required string EventId { get; init; }
}
