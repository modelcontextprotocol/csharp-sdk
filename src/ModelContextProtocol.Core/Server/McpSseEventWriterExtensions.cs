using ModelContextProtocol.Protocol;
using System.Buffers;
using System.Net.ServerSentEvents;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides MCP extension methods for <see cref="SseEventWriter"/>.
/// </summary>
internal static class McpSseEventWriterExtensions
{
    [ThreadStatic]
    private static Utf8JsonWriter? _jsonWriter;

    /// <summary>
    /// Writes an SSE item containing a <see cref="JsonRpcMessage"/>.
    /// </summary>
    /// <param name="writer">The <see cref="SseEventWriter"/>.</param>
    /// <param name="item">The SSE item containing the <see cref="JsonRpcMessage"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous write operation.</returns>
    public static ValueTask WriteAsync(this SseEventWriter writer, SseItem<JsonRpcMessage?> item, CancellationToken cancellationToken = default)
        => writer.WriteAsync(item, FormatJsonRpcMessage, cancellationToken);

    /// <summary>
    /// Writes an SSE item containing a <see cref="string"/>.
    /// </summary>
    /// <param name="writer">The <see cref="SseEventWriter"/>.</param>
    /// <param name="item">The SSE item containing the string.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous write operation.</returns>
    public static ValueTask WriteAsync(this SseEventWriter writer, SseItem<string> item, CancellationToken cancellationToken = default)
        => writer.WriteAsync(item, FormatString, cancellationToken);

    /// <summary>
    /// Formats a <see cref="JsonRpcMessage"/> message by writing it as JSON to the buffer writer.
    /// </summary>
    private static void FormatJsonRpcMessage(SseItem<JsonRpcMessage?> item, IBufferWriter<byte> writer)
    {
        if (item.Data is null)
        {
            return;
        }

        if (_jsonWriter is null)
        {
            _jsonWriter = new Utf8JsonWriter(writer);
        }
        else
        {
            _jsonWriter.Reset(writer);
        }

        JsonSerializer.Serialize(_jsonWriter, item.Data, McpJsonUtilities.JsonContext.Default.JsonRpcMessage!);
    }

    /// <summary>
    /// Formats a string by writing it as UTF-8 to the buffer writer.
    /// </summary>
    private static void FormatString(SseItem<string> item, IBufferWriter<byte> writer)
    {
        if (item.Data is null)
        {
            return;
        }

        writer.WriteUtf8String(item.Data);
    }
}
