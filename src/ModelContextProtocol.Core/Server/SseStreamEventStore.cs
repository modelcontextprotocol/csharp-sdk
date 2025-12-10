using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Wraps an <see cref="ISseEventStore"/> with session and stream context for a specific SSE stream.
/// </summary>
/// <remarks>
/// This class simplifies event storage by binding the session ID, stream ID, and retry interval
/// so that callers only need to provide the message when storing events.
/// </remarks>
internal sealed class SseStreamEventStore
{
    private readonly ISseEventStore _eventStore;
    private readonly string _sessionId;
    private readonly string _streamId;
    private readonly TimeSpan _retryInterval;

    /// <summary>
    /// Gets the retry interval to suggest to clients in SSE retry field.
    /// </summary>
    public TimeSpan RetryInterval => _retryInterval;

    /// <summary>
    /// Initializes a new instance of the <see cref="SseStreamEventStore"/> class.
    /// </summary>
    /// <param name="eventStore">The underlying event store to use for storage.</param>
    /// <param name="sessionId">The session ID, or <see langword="null"/> to generate a new one.</param>
    /// <param name="streamId">The stream ID for this SSE stream.</param>
    /// <param name="retryInterval">The retry interval to suggest to clients.</param>
    public SseStreamEventStore(ISseEventStore eventStore, string? sessionId, string streamId, TimeSpan retryInterval)
    {
        _eventStore = eventStore;
        _sessionId = sessionId ?? Guid.NewGuid().ToString("N");
        _streamId = streamId;
        _retryInterval = retryInterval;
    }

    /// <summary>
    /// Stores an event in the underlying event store with the bound session and stream context.
    /// </summary>
    /// <param name="message">The JSON-RPC message to store, or <see langword="null"/> for priming events.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The generated event ID for the stored event.</returns>
    public ValueTask<string> StoreEventAsync(JsonRpcMessage? message, CancellationToken cancellationToken = default)
        => _eventStore.StoreEventAsync(_sessionId, _streamId, message, cancellationToken);
}
