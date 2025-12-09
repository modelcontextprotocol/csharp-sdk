using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Channels;

namespace ModelContextProtocol.Server;

/// <summary>
/// Handles processing the request/response body pairs for the Streamable HTTP transport.
/// This is typically used via <see cref="JsonRpcMessageContext.RelatedTransport"/>.
/// </summary>
internal sealed class StreamableHttpPostTransport(StreamableHttpServerTransport parentTransport, Stream responseStream) : ITransport
{
    private readonly SseWriter _sseWriter = new();
    private RequestId _pendingRequest;

    public ChannelReader<JsonRpcMessage> MessageReader => throw new NotSupportedException("JsonRpcMessage.Context.RelatedTransport should only be used for sending messages.");

    string? ITransport.SessionId => parentTransport.SessionId;

    /// <returns>
    /// True, if data was written to the respond body.
    /// False, if nothing was written because the request body did not contain any <see cref="JsonRpcRequest"/> messages to respond to.
    /// The HTTP application should typically respond with an empty "202 Accepted" response in this scenario.
    /// </returns>
    public async ValueTask<bool> HandlePostAsync(JsonRpcMessage message, CancellationToken cancellationToken)
    {
        Debug.Assert(_pendingRequest.Id is null);

        if (message is JsonRpcRequest request)
        {
            _pendingRequest = request.Id;

            // Invoke the initialize request handler if applicable.
            if (request.Method == RequestMethods.Initialize)
            {
                var initializeRequest = JsonSerializer.Deserialize(request.Params, McpJsonUtilities.JsonContext.Default.InitializeRequestParams);
                await parentTransport.HandleInitRequestAsync(initializeRequest).ConfigureAwait(false);
            }
        }

        message.Context ??= new JsonRpcMessageContext();
        message.Context.RelatedTransport = this;

        // Provide callbacks for SSE stream control (SEP-1699)
        message.Context.CloseSseStream = () => _sseWriter.Complete();
        message.Context.CloseStandaloneSseStream = () => parentTransport.CloseStandaloneSseStream();

        if (parentTransport.FlowExecutionContextFromRequests)
        {
            message.Context.ExecutionContext = ExecutionContext.Capture();
        }

        await parentTransport.MessageWriter.WriteAsync(message, cancellationToken).ConfigureAwait(false);

        if (_pendingRequest.Id is null)
        {
            return false;
        }

        // Configure the SSE writer for resumability if we have an event store and the client supports it
        if (parentTransport.EventStore is not null &&
            _pendingRequest.Id is not null &&
            McpSessionHandler.SupportsResumability(parentTransport.NegotiatedProtocolVersion))
        {
            _sseWriter.EventStore = parentTransport.EventStore;
            _sseWriter.StreamId = _pendingRequest.Id.ToString();
            _sseWriter.RetryInterval = parentTransport.RetryInterval;

            // Send a priming event to establish resumability for this request
            await _sseWriter.SendPrimingEventAsync(cancellationToken).ConfigureAwait(false);
        }

        _sseWriter.MessageFilter = StopOnFinalResponseFilter;
        await _sseWriter.WriteAllAsync(responseStream, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(message);

        if (parentTransport.Stateless && message is JsonRpcRequest)
        {
            throw new InvalidOperationException("Server to client requests are not supported in stateless mode.");
        }

        bool isAccepted = await _sseWriter.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
        if (!isAccepted)
        {
            // The underlying SSE writer didn't accept the message because the stream has been closed
            // (e.g., via CloseSseStream()). If resumability is enabled, store the event so clients
            // can retrieve it when they reconnect with a Last-Event-ID header.
            if (_sseWriter.EventStore is not null && _sseWriter.StreamId is not null)
            {
                await _sseWriter.EventStore.StoreEventAsync(_sseWriter.StreamId, message, cancellationToken).ConfigureAwait(false);
            }

            // Fall back to sending via the parent transport's standalone SSE stream.
            await parentTransport.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _sseWriter.DisposeAsync().ConfigureAwait(false);
    }

    private async IAsyncEnumerable<SseItem<JsonRpcMessage?>> StopOnFinalResponseFilter(IAsyncEnumerable<SseItem<JsonRpcMessage?>> messages, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var message in messages.WithCancellation(cancellationToken))
        {
            yield return message;

            if (message.Data is JsonRpcResponse or JsonRpcError && ((JsonRpcMessageWithId)message.Data).Id == _pendingRequest)
            {
                // Complete the SSE response stream now that all pending requests have been processed.
                break;
            }
        }
    }
}
