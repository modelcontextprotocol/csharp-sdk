using System.Text;
using System.Buffers;
using System.Net.ServerSentEvents;
using System.Text.Json;
using System.Threading.Channels;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Utils.Json;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Implements the MCP SSE server transport protocol using the SSE response <see cref="Stream"/>.
/// </summary>
/// <param name="sseResponseStream">The stream to write the SSE response body to.</param>
/// <param name="messageEndpoint">The endpoint to send JSON-RPC messages to. Defaults to "/message".</param> 
public sealed class SseResponseStreamTransport(Stream sseResponseStream, string messageEndpoint = "/message") : ITransport
{
    private readonly Channel<IJsonRpcMessage> _incomingChannel = CreateBoundedChannel<IJsonRpcMessage>();
    private readonly Channel<SseItem<IJsonRpcMessage?>> _outgoingSseChannel = CreateBoundedChannel<SseItem<IJsonRpcMessage?>>();

    private Task? _sseWriteTask;
    private Utf8JsonWriter? _jsonWriter;

    /// <inheritdoc/>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// Starts the transport and writes the JSON-RPC messages sent via <see cref="SendMessageAsync(IJsonRpcMessage, CancellationToken)"/>
    /// to the SSE response stream until cancelled or disposed.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel writing to the SSE response stream.</param>
    /// <returns>A task representing the send loop that writes JSON-RPC messages to the SSE response stream.</returns>
    public Task RunAsync(CancellationToken cancellationToken)
    {
        void WriteJsonRpcMessageToBuffer(SseItem<IJsonRpcMessage?> item, IBufferWriter<byte> writer)
        {
            if (item.EventType == "endpoint")
            {
                writer.Write(Encoding.UTF8.GetBytes(messageEndpoint));
                return;
            }

            JsonSerializer.Serialize(GetUtf8JsonWriter(writer), item.Data, McpJsonUtilities.DefaultOptions.GetTypeInfo<IJsonRpcMessage?>());
        }

        IsConnected = true;

        // The very first SSE event isn't really an IJsonRpcMessage, but there's no API to write a single item of a different type,
        // so we fib and special-case the "endpoint" event type in the formatter.
        if (!_outgoingSseChannel.Writer.TryWrite(new SseItem<IJsonRpcMessage?>(null, "endpoint")))
        {
            throw new InvalidOperationException($"You must call ${nameof(RunAsync)} before calling ${nameof(SendMessageAsync)}.");
        }

        var sseItems = _outgoingSseChannel.Reader.ReadAllAsync(cancellationToken);
        return _sseWriteTask = SseFormatter.WriteAsync(sseItems, sseResponseStream, WriteJsonRpcMessageToBuffer, cancellationToken);
    }

    /// <inheritdoc/>
    public ChannelReader<IJsonRpcMessage> MessageReader => _incomingChannel.Reader;

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        _incomingChannel.Writer.TryComplete();
        _outgoingSseChannel.Writer.TryComplete();
        return new ValueTask(_sseWriteTask ?? Task.CompletedTask);
    }

    /// <inheritdoc/>
    public async Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException($"Transport is not connected. Make sure to call {nameof(RunAsync)} first.");
        }

        await _outgoingSseChannel.Writer.WriteAsync(new SseItem<IJsonRpcMessage?>(message), cancellationToken);
    }

    /// <summary>
    /// Handles incoming JSON-RPC messages received on the /message endpoint.
    /// </summary>
    /// <param name="message">The JSON-RPC message received.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the potentially asynchronous operation to buffer or process the JSON-RPC message.</returns>
    /// <exception cref="InvalidOperationException">Thrown when there is an attempt to process a message before calling <see cref="RunAsync(CancellationToken)"/>.</exception>
    public async Task OnMessageReceivedAsync(IJsonRpcMessage message, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException($"Transport is not connected. Make sure to call {nameof(RunAsync)} first.");
        }

        await _incomingChannel.Writer.WriteAsync(message, cancellationToken);
    }

    private static Channel<T> CreateBoundedChannel<T>(int capacity = 1) =>
        Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
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
