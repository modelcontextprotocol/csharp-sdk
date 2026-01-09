using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Net.ServerSentEvents;
using System.Text.Json;
using System.Threading.Channels;

namespace ModelContextProtocol.Server;

/// <summary>
/// Handles processing the request/response body pairs for the Streamable HTTP transport.
/// This is typically used via <see cref="JsonRpcMessageContext.RelatedTransport"/>.
/// </summary>
internal sealed class StreamableHttpPostTransport(StreamableHttpServerTransport parentTransport, Stream responseStream) : ITransport
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly TaskCompletionSource<bool> _responseTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SseEventWriter _sseResponseWriter = new(responseStream);

    private ISseEventStreamWriter? _sseEventStreamWriter;
    private RequestId _pendingRequest;
    private bool _disposed;

    public ChannelReader<JsonRpcMessage> MessageReader => throw new NotSupportedException("JsonRpcMessage.Context.RelatedTransport should only be used for sending messages.");

    string? ITransport.SessionId => parentTransport.SessionId;

    /// <returns>
    /// True, if data was written to the response body.
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

        if (parentTransport.FlowExecutionContextFromRequests)
        {
            message.Context.ExecutionContext = ExecutionContext.Capture();
        }

        if (_pendingRequest.Id is null)
        {
            await parentTransport.MessageWriter.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            return false;
        }

        using (await _lock.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            _sseEventStreamWriter = await parentTransport.TryCreateEventStreamAsync(streamId: _pendingRequest.Id.ToString()!, cancellationToken).ConfigureAwait(false);
            if (_sseEventStreamWriter is not null)
            {
                var primingItem = await _sseEventStreamWriter.WriteEventAsync(SseItem.Prime<JsonRpcMessage>(parentTransport.RetryInterval), cancellationToken).ConfigureAwait(false);
                await _sseResponseWriter.WriteAsync(primingItem, cancellationToken).ConfigureAwait(false);
            }

            await parentTransport.MessageWriter.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        }

        // Wait for the response to be written before returning from the handler.
        // This keeps the HTTP response open until the final response message is sent.
        await _responseTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(message);

        if (parentTransport.Stateless && message is JsonRpcRequest)
        {
            throw new InvalidOperationException("Server to client requests are not supported in stateless mode.");
        }

        using var _ = await _lock.LockAsync(cancellationToken).ConfigureAwait(false);

        var item = new SseItem<JsonRpcMessage?>(message, SseParser.EventTypeDefault);

        if (_sseEventStreamWriter is not null)
        {
            item = await _sseEventStreamWriter.WriteEventAsync(item, cancellationToken).ConfigureAwait(false);
        }

        if (!_disposed)
        {
            // Only write the message to the response if we're not disposed, since disposal is a sign
            // that the response has completed.

            try
            {
                await _sseResponseWriter.WriteAsync(item, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _responseTcs.TrySetException(ex);
            }
        }
        else if (_sseEventStreamWriter is null)
        {
            // The response has completed, and there is no event stream to store the message.
            // Rather than drop the message, fall back to sending it via the parent transport.
            await parentTransport.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }

        // Complete the response if this is the final message.
        if ((message is JsonRpcResponse or JsonRpcError) && ((JsonRpcMessageWithId)message).Id == _pendingRequest)
        {
            if (_sseEventStreamWriter is not null)
            {
                await _sseEventStreamWriter.DisposeAsync().ConfigureAwait(false);
                _sseEventStreamWriter = null;
            }

            _responseTcs.TrySetResult(true);
        }
    }

    public async ValueTask EnablePollingAsync(TimeSpan retryInterval, CancellationToken cancellationToken)
    {
        if (parentTransport.Stateless)
        {
            throw new InvalidOperationException("Polling is not supported in stateless mode.");
        }

        using var _ = await _lock.LockAsync(cancellationToken).ConfigureAwait(false);

        if (_sseEventStreamWriter is null)
        {
            throw new InvalidOperationException($"Polling requires an event stream store to be configured.");
        }

        // Send the priming event with the new retry interval.
        var primingItem = await _sseEventStreamWriter.WriteEventAsync(SseItem.Prime<JsonRpcMessage>(retryInterval), cancellationToken).ConfigureAwait(false);

        // Write to the response stream if it still exists.
        if (!_disposed)
        {
            await _sseResponseWriter.WriteAsync(primingItem, cancellationToken).ConfigureAwait(false);
        }

        // Set the mode to 'Polling' so that the replay stream ends as soon as all available messages have been sent.
        // This prevents the client from immediately establishing another long-lived connection.
        await _sseEventStreamWriter.SetModeAsync(SseEventStreamMode.Polling, cancellationToken).ConfigureAwait(false);

        // Signal completion so HandlePostAsync can return.
        _responseTcs.TrySetResult(true);
    }

    public async ValueTask DisposeAsync()
    {
        using var _ = await _lock.LockAsync().ConfigureAwait(false);

        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _responseTcs.TrySetResult(true);

        _sseResponseWriter.Dispose();

        // Don't dispose the event stream writer here, as we may continue to write to the event store
        // after disposal if there are pending messages.
    }
}
