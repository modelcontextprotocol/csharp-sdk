using ModelContextProtocol.Protocol;
using System.Buffers;
using System.Net.ServerSentEvents;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides extension methods for <see cref="ISseEventStreamReader"/>.
/// </summary>
public static class SseEventStreamReaderExtensions
{
    /// <summary>
    /// Copies all events from the reader to the destination stream in SSE format.
    /// </summary>
    /// <param name="reader">The event stream reader to copy events from.</param>
    /// <param name="destination">The destination stream to write SSE-formatted events to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous copy operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reader"/> or <paramref name="destination"/> is null.</exception>
    public static async Task CopyToAsync(this ISseEventStreamReader reader, Stream destination, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(reader);
        Throw.IfNull(destination);

        Utf8JsonWriter? jsonWriter = null;

        var events = reader.ReadEventsAsync(cancellationToken);
        await SseFormatter.WriteAsync(events, destination, FormatEvent, cancellationToken);

        void FormatEvent(SseItem<JsonRpcMessage?> item, IBufferWriter<byte> writer)
        {
            if (item.Data is null)
            {
                return;
            }

            if (jsonWriter is null)
            {
                jsonWriter = new Utf8JsonWriter(writer);
            }
            else
            {
                jsonWriter.Reset(writer);
            }

            JsonSerializer.Serialize(jsonWriter, item.Data, McpJsonUtilities.JsonContext.Default.JsonRpcMessage!);
        }
    }
}
