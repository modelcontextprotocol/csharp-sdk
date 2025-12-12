using ModelContextProtocol.Protocol;
using System.Net.ServerSentEvents;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides read access to an SSE event stream, allowing events to be consumed asynchronously.
/// </summary>
public interface ISseEventStreamReader
{
    /// <summary>
    /// Gets the session ID associated with the stream being read.
    /// </summary>
    string SessionId { get; }

    /// <summary>
    /// Gets the ID of the stream.
    /// </summary>
    /// <remarks>
    /// This value is guaranteed to be unique on a per-session basis.
    /// </remarks>
    string StreamId { get; }

    /// <summary>
    /// Gets the messages from the stream as an <see cref="IAsyncEnumerable{T}"/>.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An <see cref="IAsyncEnumerable{T}"/> of <see cref="SseItem{T}"/> containing JSON-RPC messages.</returns>
    /// <remarks>
    /// If the stream's mode is set to <see cref="SseEventStreamMode.Polling"/>, the returned
    /// messages will only include the currently-available events starting at the last event ID specified
    /// when the reader was created. Otherwise, the returned messages will continue until the associated
    /// <see cref="ISseEventStreamWriter"/> is disposed.
    /// </remarks>
    IAsyncEnumerable<SseItem<JsonRpcMessage?>> ReadEventsAsync(CancellationToken cancellationToken = default);
}
