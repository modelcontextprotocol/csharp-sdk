using System.Text;
using System.Buffers;
using System.Net.ServerSentEvents;
using System.Text.Json;
using System.Threading.Channels;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Utils.Json;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Implements the MCP transport protocol using Server-Sent Events (SSE) for server-to-client communication.
/// </summary>
/// <remarks>
/// <para>
/// This transport provides one-way communication from server to client using the SSE protocol over HTTP,
/// while receiving client messages through a separate webhook mechanism. It writes JSON-RPC messages as 
/// SSE events to a response stream typically associated with an HTTP response.
/// </para>
/// <para>
/// This transport is used in scenarios where the server needs to push messages to the client in real-time,
/// such as when streaming completion results or providing progress updates during long-running operations.
/// </para>
/// </remarks>
/// <param name="sseResponseStream">The stream to write the SSE response body to.</param>
/// <param name="messageEndpoint">The endpoint to send JSON-RPC messages to. Defaults to "/message".</param> 
public sealed class SseResponseStreamTransport(Stream sseResponseStream, string messageEndpoint = "/message") : ITransport
{
    private readonly Channel<IJsonRpcMessage> _incomingChannel = CreateBoundedChannel<IJsonRpcMessage>();
    private readonly Channel<SseItem<IJsonRpcMessage?>> _outgoingSseChannel = CreateBoundedChannel<SseItem<IJsonRpcMessage?>>();

    private Task? _sseWriteTask;
    private Utf8JsonWriter? _jsonWriter;

    /// <summary>
    /// Gets whether the transport is currently connected and able to send/receive messages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property is <c>true</c> after <see cref="RunAsync(CancellationToken)"/> is called and until the 
    /// transport is disconnected. Methods like <see cref="SendMessageAsync(IJsonRpcMessage, CancellationToken)"/> and
    /// <see cref="ReceiveMessageAsync(CancellationToken)"/> can only be used while the transport is connected.
    /// </para>
    /// </remarks>
    /// and the transport is actively processing messages.
    /// </para>
    /// <para>
    /// It becomes <c>false</c> when the transport is disposed or encounters an error that
    /// prevents further communication.
    /// </para>
    /// <para>
    /// Methods like <see cref="SendMessageAsync(IJsonRpcMessage, CancellationToken)"/> and
    /// <see cref="OnMessageReceivedAsync(IJsonRpcMessage, CancellationToken)"/> will throw an
    /// <see cref="InvalidOperationException"/> if called when this property is <c>false</c>.
    /// </para>
    /// </remarks>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// Starts the transport and writes the JSON-RPC messages sent via <see cref="SendMessageAsync(IJsonRpcMessage, CancellationToken)"/>
    /// to the SSE response stream until cancelled or disposed.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the send loop that writes JSON-RPC messages to the SSE response stream.</returns>
    public Task RunAsync(CancellationToken cancellationToken)
    {
        // The very first SSE event isn't really an IJsonRpcMessage, but there's no API to write a single item of a different type,
        // so we fib and special-case the "endpoint" event type in the formatter.
        if (!_outgoingSseChannel.Writer.TryWrite(new SseItem<IJsonRpcMessage?>(null, "endpoint")))
        {
            throw new InvalidOperationException($"You must call ${nameof(RunAsync)} before calling ${nameof(SendMessageAsync)}.");
        }

        IsConnected = true;

        var sseItems = _outgoingSseChannel.Reader.ReadAllAsync(cancellationToken);
        return _sseWriteTask = SseFormatter.WriteAsync(sseItems, sseResponseStream, WriteJsonRpcMessageToBuffer, cancellationToken);
    }

    private void WriteJsonRpcMessageToBuffer(SseItem<IJsonRpcMessage?> item, IBufferWriter<byte> writer)
    {
        if (item.EventType == "endpoint")
        {
            writer.Write(Encoding.UTF8.GetBytes(messageEndpoint));
            return;
        }

        JsonSerializer.Serialize(GetUtf8JsonWriter(writer), item.Data, McpJsonUtilities.JsonContext.Default.IJsonRpcMessage!);
    }

    /// <inheritdoc/>
    public ChannelReader<IJsonRpcMessage> MessageReader => _incomingChannel.Reader;

    /// <inheritdoc/>
    /// <summary>
    /// Asynchronously releases all resources used by the SSE response stream transport.
    /// </summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    /// <remarks>
    /// This method completes both the incoming and outgoing channels to stop message processing,
    /// and waits for any ongoing SSE write operations to complete.
    /// After disposal, <see cref="IsConnected"/> will return false and any attempt to send
    /// messages will throw an <see cref="InvalidOperationException"/>.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        _incomingChannel.Writer.TryComplete();
        _outgoingSseChannel.Writer.TryComplete();
        return new ValueTask(_sseWriteTask ?? Task.CompletedTask);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// For SSE response streams, this method writes the message to the outgoing SSE channel using the default
    /// "message" event type. This is the primary mechanism for server-to-client communication in the SSE transport model.
    /// </para>
    /// <para>
    /// The method requires that <see cref="RunAsync"/> has been called and the transport is connected.
    /// Messages sent through this method will be formatted as SSE events and transmitted to the client.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when attempting to send a message before calling <see cref="RunAsync"/>.
    /// </exception>
    public async Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException($"Transport is not connected. Make sure to call {nameof(RunAsync)} first.");
        }

        // Emit redundant "event: message" lines for better compatibility with other SDKs.
        await _outgoingSseChannel.Writer.WriteAsync(new SseItem<IJsonRpcMessage?>(message, SseParser.EventTypeDefault), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles incoming JSON-RPC messages received on the /message endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method is the entry point for processing client-to-server communication in the SSE transport model. 
    /// While the SSE protocol itself is unidirectional (server to client), this method allows bidirectional 
    /// communication by handling HTTP POST requests sent to the message endpoint.
    /// </para>
    /// <para>
    /// When a client sends a JSON-RPC message to the /message endpoint, the server calls this method to
    /// process the message and make it available to the MCP server via the <see cref="MessageReader"/> channel.
    /// </para>
    /// <para>
    /// This method validates that the transport is connected before processing the message, ensuring proper
    /// sequencing of operations in the transport lifecycle.
    /// </para>
    /// </remarks>
    /// <param name="message">The JSON-RPC message received from the client.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the asynchronous operation to buffer the JSON-RPC message for processing.</returns>
    /// <exception cref="InvalidOperationException">Thrown when there is an attempt to process a message before calling <see cref="RunAsync(CancellationToken)"/>.</exception>
    public async Task OnMessageReceivedAsync(IJsonRpcMessage message, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException($"Transport is not connected. Make sure to call {nameof(RunAsync)} first.");
        }

        await _incomingChannel.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
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
