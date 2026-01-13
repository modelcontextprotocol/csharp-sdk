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
    private readonly SemaphoreSlim _messageLock = new(1, 1);
    private readonly TaskCompletionSource<bool> _responseTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SseEventWriter _sseResponseWriter = new(responseStream);

    private TaskCompletionSource<bool>? _streamTcs;
    private ISseEventStreamWriter? _sseEventStreamWriter;
    private RequestId _pendingRequest;
    private bool _finalResponseMessageSent;
    private bool _originalResponseCompleted;

    public ChannelReader<JsonRpcMessage> MessageReader => throw new NotSupportedException("JsonRpcMessage.Context.RelatedTransport should only be used for sending messages.");

    string? ITransport.SessionId => parentTransport.SessionId;

    /// <returns>
    /// True, if data was written to the response body.
    /// False, if nothing was written because the request body did not contain any <see cref="JsonRpcRequest"/> messages to respond to.
    /// The HTTP application should typically respond with an empty "202 Accepted" response in this scenario.
    /// </returns>
    public async ValueTask<bool> HandlePostAsync(JsonRpcMessage message, CancellationToken postCancellationToken, CancellationToken sessionCancellationToken)
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
            await parentTransport.MessageWriter.WriteAsync(message, postCancellationToken).ConfigureAwait(false);
            return false;
        }

        using (await _messageLock.LockAsync(postCancellationToken).ConfigureAwait(false))
        {
            var primingItem = await TryStartSseEventStreamAsync(_pendingRequest, sessionCancellationToken).ConfigureAwait(false);
            if (primingItem.HasValue)
            {
                await _sseResponseWriter.WriteAsync(primingItem.Value, postCancellationToken).ConfigureAwait(false);
            }

            // Ensure that we've sent the priming event before processing the incoming request.
            await parentTransport.MessageWriter.WriteAsync(message, postCancellationToken).ConfigureAwait(false);
        }

        // Wait for the response to be written before returning from the handler.
        // This keeps the HTTP response open until the final response message is sent.
        await _responseTcs.Task.WaitAsync(postCancellationToken).ConfigureAwait(false);

        return true;
    }

    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(message);

        if (parentTransport.Stateless && message is JsonRpcRequest)
        {
            throw new InvalidOperationException("Server to client requests are not supported in stateless mode.");
        }

        using var _ = await _messageLock.LockAsync().ConfigureAwait(false);

        try
        {

            if (_finalResponseMessageSent)
            {
                // The final response message has already been sent.
                // Rather than drop the message, fall back to sending it via the parent transport.
                await parentTransport.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
                return;
            }

            var item = new SseItem<JsonRpcMessage?>(message, SseParser.EventTypeDefault);

            if (_sseEventStreamWriter is not null)
            {
                item = await _sseEventStreamWriter.WriteEventAsync(item, cancellationToken).ConfigureAwait(false);
            }

            if (!_originalResponseCompleted)
            {
                // Only write the message to the response if the response has not completed.
                try
                {
                    await _sseResponseWriter.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _responseTcs.TrySetException(ex);
                }
            }
        }
        finally
        {
            // Complete the response if this is the final message.
            if ((message is JsonRpcResponse or JsonRpcError) && ((JsonRpcMessageWithId)message).Id == _pendingRequest)
            {
                _finalResponseMessageSent = true;
                _responseTcs.TrySetResult(true);
                _streamTcs?.TrySetResult(true);
            }
        }
    }

    public async ValueTask EnablePollingAsync(TimeSpan retryInterval, CancellationToken cancellationToken)
    {
        if (parentTransport.Stateless)
        {
            throw new InvalidOperationException("Polling is not supported in stateless mode.");
        }

        using var _ = await _messageLock.LockAsync(cancellationToken).ConfigureAwait(false);

        if (_sseEventStreamWriter is null)
        {
            throw new InvalidOperationException($"Polling requires an event stream store to be configured.");
        }

        // Send the priming event with the new retry interval.
        var primingItem = await _sseEventStreamWriter.WriteEventAsync(
            sseItem: new SseItem<JsonRpcMessage?>() { ReconnectionInterval = retryInterval },
            cancellationToken)
            .ConfigureAwait(false);

        // Write to the response stream if it still exists.
        if (!_originalResponseCompleted)
        {
            await _sseResponseWriter.WriteAsync(primingItem, cancellationToken).ConfigureAwait(false);
        }

        // Set the mode to 'Polling' so that the replay stream ends as soon as all available messages have been sent.
        // This prevents the client from immediately establishing another long-lived connection.
        await _sseEventStreamWriter.SetModeAsync(SseEventStreamMode.Polling, cancellationToken).ConfigureAwait(false);

        // Signal completion so HandlePostAsync can return.
        _responseTcs.TrySetResult(true);
    }

    private async ValueTask<SseItem<JsonRpcMessage?>?> TryStartSseEventStreamAsync(RequestId requestId, CancellationToken cancellationToken)
    {
        Debug.Assert(_sseEventStreamWriter is null);

        _sseEventStreamWriter = await parentTransport.TryCreateEventStreamAsync(
            streamId: requestId.Id!.ToString()!,
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (_sseEventStreamWriter is null)
        {
            return null;
        }

        _streamTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = HandleStreamWriterDisposalAsync(_streamTcs.Task, cancellationToken);

        return await _sseEventStreamWriter.WriteEventAsync(SseItem.Prime<JsonRpcMessage>(), cancellationToken).ConfigureAwait(false);

        async Task HandleStreamWriterDisposalAsync(Task streamTask, CancellationToken cancellationToken)
        {
            try
            {
                await streamTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                using var _ = await _messageLock.LockAsync().ConfigureAwait(false);

                await _sseEventStreamWriter!.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        using var _ = await _messageLock.LockAsync().ConfigureAwait(false);

        if (_originalResponseCompleted)
        {
            return;
        }

        _originalResponseCompleted = true;

        _responseTcs.TrySetResult(true);

        _sseResponseWriter.Dispose();

        // Don't dispose the event stream writer here, as we may continue to write to the event store
        // after disposal if there are pending messages.
    }
}
