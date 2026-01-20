namespace System.Net.ServerSentEvents;

/// <summary>
/// Provides factory methods for creating server-sent event (SSE) items with specific event types and data payloads.
/// </summary>
internal static class SseItem
{
    /// <summary>
    /// Creates a new server-sent event (SSE) message containing the specified data and the default event type.
    /// </summary>
    /// <typeparam name="T">The type of the data to include in the SSE message.</typeparam>
    /// <param name="data">The data to include in the SSE message. Can be null.</param>
    /// <returns>An <see cref="SseItem{T}"/> representing an SSE message with the specified data and the default event type.</returns>
    public static SseItem<T?> Message<T>(T? data)
        => new(data: data, SseParser.EventTypeDefault);

    /// <summary>
    /// Creates a new Server-Sent Events (SSE) item representing a 'prime' event with no data.
    /// </summary>
    /// <returns>An <see cref="SseItem{T}"/> instance representing a 'prime' event with no data.</returns>
    public static SseItem<T?> Prime<T>()
        => new(data: default, eventType: "prime");

    /// <summary>
    /// Creates a server-sent event (SSE) item representing the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint string to include in the SSE item. Cannot be null.</param>
    /// <returns>An <see cref="SseItem{String}"/> containing the specified endpoint value.</returns>
    public static SseItem<string> Endpoint(string endpoint)
        => new(endpoint, "endpoint");
}
