using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
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
    private readonly SemaphoreSlim _eventStreamLock = new(1, 1);
    private ISseEventStreamWriter? _eventStreamWriter;
    private RequestId _pendingRequest;

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

        // When applicable, we start the write task as soon as possible so that:
        // 1. We don't risk processing the final response message and closing the _sseWriter channel before starting to write to the response stream.
        // 2. We don't risk deadlocking by filling up the _sseWriter channel with messages before they start being consumed.
        var shouldWriteToResponseStream = _pendingRequest.Id is not null;
        var writeTask = shouldWriteToResponseStream
            ? _sseWriter.WriteAllAsync(responseStream, cancellationToken)
            : Task.CompletedTask;

        await parentTransport.MessageWriter.WriteAsync(message, cancellationToken).ConfigureAwait(false);

        if (!shouldWriteToResponseStream)
        {
            return false;
        }

        var eventStreamWriter = await GetOrCreateEventStreamAsync(cancellationToken).ConfigureAwait(false);
        if (eventStreamWriter is not null)
        {
            await _sseWriter.SendPrimingEventAsync(parentTransport.RetryInterval, eventStreamWriter, cancellationToken).ConfigureAwait(false);
        }

        await writeTask.ConfigureAwait(false);
        return true;
    }

    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(message);

        if (parentTransport.Stateless && message is JsonRpcRequest)
        {
            throw new InvalidOperationException("Server to client requests are not supported in stateless mode.");
        }

        var eventStreamWriter = await GetOrCreateEventStreamAsync(cancellationToken).ConfigureAwait(false);

        var isAccepted = await _sseWriter.SendMessageAsync(message, eventStreamWriter, cancellationToken).ConfigureAwait(false);
        if (!isAccepted && eventStreamWriter is null)
        {
            // The underlying writer didn't accept the message because the underlying request has completed,
            // and there isn't a fallback event stream writer.
            // Rather than drop the message, fall back to sending it via the parent transport.
            await parentTransport.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }

        if ((message is JsonRpcResponse or JsonRpcError) && ((JsonRpcMessageWithId)message).Id == _pendingRequest)
        {
            // Complete the SSE response stream and event stream writer now that all pending requests have been processed.
            Debug.Assert(_sseWriter.WriteTask is not null, "Unexpectedly processed the final response message without writing to the response stream.");

            await _sseWriter.CompleteAsync(cancellationToken).ConfigureAwait(false);

            if (_eventStreamWriter is not null)
            {
                await _eventStreamWriter.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async ValueTask EnablePollingAsync(TimeSpan retryInterval, CancellationToken cancellationToken)
    {
        if (parentTransport.Stateless)
        {
            // Polling is currently not supported in stateless mode.
            return;
        }

        var eventStreamWriter = await GetOrCreateEventStreamAsync(cancellationToken).ConfigureAwait(false);
        if (eventStreamWriter is null)
        {
            return;
        }

        // Set the mode to 'Polling' so that the replay stream ends as soon as all available messages have been sent.
        // This prevents the client from immediately establishing another long-lived connection.
        await eventStreamWriter.SetModeAsync(SseEventStreamMode.Polling, cancellationToken).ConfigureAwait(false);

        // Send the priming event with the new retry interval.
        await _sseWriter.SendPrimingEventAsync(retryInterval, eventStreamWriter, cancellationToken).ConfigureAwait(false);

        // Close the writer and force future writes to only apply to the SSE event store.
        await _sseWriter.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _sseWriter.DisposeAsync().ConfigureAwait(false);

        // Don't dispose the event stream writer here, as we may continue to write to the event store
        // after disposal.
    }

    private async ValueTask<ISseEventStreamWriter?> GetOrCreateEventStreamAsync(CancellationToken cancellationToken)
    {
        using var _ = await _eventStreamLock.LockAsync(cancellationToken).ConfigureAwait(false);

        if (_eventStreamWriter is not null)
        {
            return _eventStreamWriter;
        }

        if (parentTransport.EventStreamStore is not { } eventStreamStore || _pendingRequest.Id is null || !McpSessionHandler.SupportsPrimingEvent(parentTransport.NegotiatedProtocolVersion))
        {
            return null;
        }

        // We use the 'Default' stream mode so that in the case of an unexpected network disconnection,
        // the client can continue reading the remaining messages in a single, streamed response.
        // This may be changed to 'Polling' if the transport is later explicitly switched to polling mode.
        const SseEventStreamMode Mode = SseEventStreamMode.Default;

        _eventStreamWriter = await eventStreamStore.CreateStreamAsync(options: new()
        {
            SessionId = parentTransport.SessionId ?? Guid.NewGuid().ToString("N"),
            StreamId = _pendingRequest.Id.ToString()!,
            Mode = Mode,
        }, cancellationToken).ConfigureAwait(false);

        return _eventStreamWriter;
    }
}
