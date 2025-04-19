using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.Buffers;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace ModelContextProtocol.Protocol.Transport;

internal sealed class SseWriter(string? messageEndpoint = null, BoundedChannelOptions? channelOptions = null) : IAsyncDisposable
{
    private readonly Channel<SseItem<JsonRpcMessage?>> _messages = Channel.CreateBounded<SseItem<JsonRpcMessage?>>(channelOptions ?? new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
    });

    private Utf8JsonWriter? _jsonWriter;
    private Task? _writeTask;
    private CancellationToken? _writeCancellationToken;

    private readonly SemaphoreSlim _disposeLock = new(1, 1);
    private bool _disposed;

    public Func<IAsyncEnumerable<SseItem<JsonRpcMessage?>>, CancellationToken, IAsyncEnumerable<SseItem<JsonRpcMessage?>>>? MessageFilter { get; set; }

    public Task WriteAllAsync(Stream sseResponseStream, CancellationToken cancellationToken)
    {
        // When messageEndpoint is set, the very first SSE event isn't really an IJsonRpcMessage, but there's no API to write a single
        // item of a different type, so we fib and special-case the "endpoint" event type in the formatter.
        if (messageEndpoint is not null && !_messages.Writer.TryWrite(new SseItem<JsonRpcMessage?>(null, "endpoint")))
        {
            throw new InvalidOperationException("You must call RunAsync before calling SendMessageAsync.");
        }

        _writeCancellationToken = cancellationToken;

        var messages = _messages.Reader.ReadAllAsync(cancellationToken);
        if (MessageFilter is not null)
        {
            messages = MessageFilter(messages, cancellationToken);
        }

        _writeTask = SseFormatter.WriteAsync(messages, sseResponseStream, WriteJsonRpcMessageToBuffer, cancellationToken);
        return _writeTask;
    }

    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(message);

        using var _ = await _disposeLock.LockAsync(cancellationToken).ConfigureAwait(false);

        if (_disposed)
        {
            // Don't throw an ODE, because this is disposed internally when the transport disconnects due to an abort
            // or sending all the responses for the a give given Streamable HTTP POST request, so the user might not be at fault.
            // There's precedence for no-oping here similar to writing to the response body of an aborted request in ASP.NET Core.
            return;
        }

        // Emit redundant "event: message" lines for better compatibility with other SDKs.
        await _messages.Writer.WriteAsync(new SseItem<JsonRpcMessage?>(message, SseParser.EventTypeDefault), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        using var _ = await _disposeLock.LockAsync().ConfigureAwait(false);

        if (_disposed)
        {
            return;
        }

        _messages.Writer.Complete();
        try
        {
            if (_writeTask is not null)
            {
                await _writeTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_writeCancellationToken?.IsCancellationRequested == true)
        {
            // Ignore exceptions caused by intentional cancellation during shutdown.
        }
        finally
        {
            _jsonWriter?.Dispose();
            _disposed = true;
        }
    }

    private void WriteJsonRpcMessageToBuffer(SseItem<JsonRpcMessage?> item, IBufferWriter<byte> writer)
    {
        if (item.EventType == "endpoint" && messageEndpoint is not null)
        {
            writer.Write(Encoding.UTF8.GetBytes(messageEndpoint));
            return;
        }

        JsonSerializer.Serialize(GetUtf8JsonWriter(writer), item.Data, McpJsonUtilities.JsonContext.Default.JsonRpcMessage!);
    }

    private Utf8JsonWriter GetUtf8JsonWriter(IBufferWriter<byte> writer)
    {
        if (_jsonWriter is null)
        {
            _jsonWriter = new Utf8JsonWriter(writer);
        }
        else
        {
            _jsonWriter.Reset(writer);
        }

        return _jsonWriter;
    }
}
