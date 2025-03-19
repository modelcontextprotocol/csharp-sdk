using System.Buffers;
using System.Net.ServerSentEvents;
using System.Text.Json;
using System.Threading.Channels;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Utils.Json;

namespace AspNetCoreSseServer;

public class SseServerStreamTransport(Stream sseResponseStream) : ITransport
{
    private readonly Channel<IJsonRpcMessage> _incomingChannel = CreateSingleItemChannel<IJsonRpcMessage>();
    private readonly Channel<SseItem<IJsonRpcMessage?>> _outgoingSseChannel = CreateSingleItemChannel<SseItem<IJsonRpcMessage?>>();

    private Task? _sseWriteTask;
    private Utf8JsonWriter? _jsonWriter;

    public bool IsConnected => _sseWriteTask?.IsCompleted == false;

    public Task RunAsync(CancellationToken cancellationToken)
    {
        void WriteJsonRpcMessageToBuffer(SseItem<IJsonRpcMessage?> item, IBufferWriter<byte> writer)
        {
            if (item.EventType == "endpoint")
            {
                writer.Write("/message"u8);
                return;
            }

            JsonSerializer.Serialize(GetUtf8JsonWriter(writer), item.Data, McpJsonUtilities.DefaultOptions);
        }

        // The very first SSE event isn't really an IJsonRpcMessage, but there's no API to write a single item of a different type,
        // so we fib and special-case the "endpoint" event type in the formatter.
        _outgoingSseChannel.Writer.TryWrite(new SseItem<IJsonRpcMessage?>(null, "endpoint"));

        var sseItems = _outgoingSseChannel.Reader.ReadAllAsync(cancellationToken);
        return _sseWriteTask = SseFormatter.WriteAsync(sseItems, sseResponseStream, WriteJsonRpcMessageToBuffer, cancellationToken);
    }

    public ChannelReader<IJsonRpcMessage> MessageReader => _incomingChannel.Reader;

    public ValueTask DisposeAsync()
    {
        _incomingChannel.Writer.TryComplete();
        _outgoingSseChannel.Writer.TryComplete();
        return new ValueTask(_sseWriteTask ?? Task.CompletedTask);
    }

    public Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default) =>
        _outgoingSseChannel.Writer.WriteAsync(new SseItem<IJsonRpcMessage?>(message), cancellationToken).AsTask();

    public Task OnMessageReceivedAsync(IJsonRpcMessage message, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            throw new McpTransportException("Transport is not connected");
        }

        return _incomingChannel.Writer.WriteAsync(message, cancellationToken).AsTask();
    }

    private static Channel<T> CreateSingleItemChannel<T>() =>
        Channel.CreateBounded<T>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
        });

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
