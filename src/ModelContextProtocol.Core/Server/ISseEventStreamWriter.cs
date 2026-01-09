using ModelContextProtocol.Protocol;
using System.Net.ServerSentEvents;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides write access to an SSE event stream, allowing events to be written and tracked with unique IDs.
/// </summary>
public interface ISseEventStreamWriter : IAsyncDisposable
{
    /// <summary>
    /// Sets the mode of the event stream.
    /// </summary>
    /// <param name="mode">The new mode to set for the event stream.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask SetModeAsync(SseEventStreamMode mode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes an event to the stream.
    /// </summary>
    /// <param name="sseItem">The original <see cref="SseItem{T}"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A new <see cref="SseItem{T}"/> with a populated event ID.</returns>
    /// <remarks>
    /// If the provided <paramref name="sseItem"/> already has an event ID, this method skips writing the event.
    /// Otherwise, an event ID unique to all sessions and streams is generated and assigned to the event.
    /// </remarks>
    ValueTask<SseItem<JsonRpcMessage?>> WriteEventAsync(SseItem<JsonRpcMessage?> sseItem, CancellationToken cancellationToken = default);
}
