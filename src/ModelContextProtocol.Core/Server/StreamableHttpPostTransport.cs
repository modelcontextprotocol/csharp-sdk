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
internal sealed class StreamableHttpPostTransport(StreamableHttpServerTransport parentTransport, Stream? responseStream) : ITransport
{
    private readonly SseWriter _sseWriter = new();
    private RequestId _pendingRequest;

    public ChannelReader<JsonRpcMessage> MessageReader => throw new NotSupportedException("JsonRpcMessage.Context.RelatedTransport should only be used for sending messages.");

    string? ITransport.SessionId => parentTransport.SessionId;

    /// <returns>
    /// An async enumerable of JSON-RPC messages to be sent back to the client.
    /// If the request body did not contain any <see cref="JsonRpcRequest"/> messages to respond to,
    /// the enumerable will be empty.
    /// </returns>
    public async IAsyncEnumerable<JsonRpcMessage> HandlePostAsync(JsonRpcMessage message, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Debug.Assert(_pendingRequest.Id is null);

        if (message is JsonRpcRequest request)
        {
            _pendingRequest = request.Id;

            // Invoke the initialize request callback if applicable.
            if (parentTransport.OnInitRequestReceived is { } onInitRequest && request.Method == RequestMethods.Initialize)
            {
                var initializeRequest = JsonSerializer.Deserialize(request.Params, McpJsonUtilities.JsonContext.Default.InitializeRequestParams);
                await onInitRequest(initializeRequest).ConfigureAwait(false);
            }
        }

        message.Context ??= new JsonRpcMessageContext();
        message.Context.RelatedTransport = this;

        if (parentTransport.FlowExecutionContextFromRequests)
        {
            message.Context.ExecutionContext = ExecutionContext.Capture();
        }

        await parentTransport.MessageWriter.WriteAsync(message, cancellationToken).ConfigureAwait(false);

        if (_pendingRequest.Id is null)
        {
            yield break;
        }

        if (responseStream is not null)
        {
            // Legacy path: write to stream
            _sseWriter.MessageFilter = StopOnFinalResponseFilter;
            await _sseWriter.WriteAllAsync(responseStream, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // New path: yield messages directly
            _sseWriter.MessageFilter = StopOnFinalResponseFilter;
            await foreach (var sseItem in _sseWriter.ReadMessagesAsync(cancellationToken).ConfigureAwait(false))
            {
                if (sseItem.Data is not null)
                {
                    yield return sseItem.Data;
                }
            }
        }
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
            // The underlying writer didn't accept the message because the underlying request has completed.
            // Rather than drop the message, fall back to sending it via the parent transport.
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
