namespace ModelContextProtocol.Server;

/// <summary>
/// Provides storage and retrieval of SSE event streams, enabling resumability and redelivery of events.
/// </summary>
public interface ISseEventStreamStore
{
    /// <summary>
    /// Creates a new SSE event stream with the specified options.
    /// </summary>
    /// <param name="options">The configuration options for the new stream.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A writer for the newly created event stream.</returns>
    ValueTask<ISseEventStreamWriter> CreateStreamAsync(SseEventStreamOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a reader for an existing event stream based on the last event ID.
    /// </summary>
    /// <param name="lastEventId">The ID of the last event received by the client, used to resume from that point.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A reader for the event stream, or <c>null</c> if no matching stream is found.</returns>
    ValueTask<ISseEventStreamReader?> GetStreamReaderAsync(string lastEventId, CancellationToken cancellationToken = default);
}
