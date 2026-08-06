using Microsoft.Extensions.Logging;
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
internal sealed partial class StreamableHttpPostTransport(
    StreamableHttpServerTransport parentTransport,
    Stream responseStream,
    CancellationToken sessionCancellationToken,
    ILogger logger,
    StreamableHttpResponseStartOptions? responseStartOptions = null) : ITransport
{
    private readonly SemaphoreSlim _messageLock = new(1, 1);
    private readonly TaskCompletionSource<bool> _httpResponseTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SseEventWriter _httpSseWriter = new(responseStream);

    private TaskCompletionSource<bool>? _storeStreamTcs;
    private List<SseItem<JsonRpcMessage?>>? _pendingHttpSseItems;
#pragma warning disable MCP9006 // Stateful Streamable HTTP resumability types are obsolete but still wired up internally.
    private ISseEventStreamWriter? _storeSseWriter;
#pragma warning restore MCP9006

    private RequestId _pendingRequest;
    private bool _finalResponseMessageSent;
    private bool _httpResponseCompleted;
    private bool _httpResponseStarted;

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

        message.Context ??= new JsonRpcMessageContext();

        if (message is JsonRpcRequest request)
        {
            _pendingRequest = request.Id;
            message.Context.RelatedTransport = this;

            // Invoke the initialize request handler if applicable. On a per-request-metadata
            // protocol revision (2026-07-28+) initialize is a removed method: skip the eager
            // params deserialization (whose required properties would throw on an arbitrary
            // payload) and let the session's protocol boundary reject the request with
            // Method not found.
            if (request.Method == RequestMethods.Initialize &&
                !McpProtocolVersions.RequiresPerRequestMetadata(message.Context.ProtocolVersion))
            {
                var initializeRequest = JsonSerializer.Deserialize(request.Params, McpJsonUtilities.JsonContext.Default.InitializeRequestParams);
                await parentTransport.HandleInitializeRequestAsync(initializeRequest).ConfigureAwait(false);
            }
        }

        if (parentTransport.FlowExecutionContextFromRequests)
        {
            message.Context.ExecutionContext = ExecutionContext.Capture();
        }

        if (_pendingRequest.Id is null)
        {
            await parentTransport.MessageWriter.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            return false;
        }

        using (await _messageLock.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            var primingItem = await TryStartSseEventStreamAsync(_pendingRequest).ConfigureAwait(false);
            if (primingItem.HasValue)
            {
                if (responseStartOptions is null)
                {
                    StartHttpResponse(firstMessage: null);
                    await _httpSseWriter.WriteAsync(primingItem.Value, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    QueuePendingHttpSseItem(primingItem.Value);
                }
            }
            else if (responseStartOptions is null)
            {
                // If there's no priming write, flush the stream to ensure HTTP response headers are
                // sent to the client now that the server is ready to process the request.
                // This prevents HttpClient timeout for long-running requests.
                StartHttpResponse(firstMessage: null);
                await responseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            // Legacy responses send the priming event before processing the request. A response
            // waiting for its first JSON-RPC message keeps the event queued until that message arrives.
            await parentTransport.MessageWriter.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        }

        // Wait for the response to be written before returning from the handler.
        // This keeps the HTTP response open until the final response message is sent.
        await _httpResponseTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Notifies the HTTP application exactly once, immediately before the first response write.
    /// </summary>
    private void StartHttpResponse(JsonRpcMessage? firstMessage)
    {
        if (_httpResponseStarted)
        {
            return;
        }

        _httpResponseStarted = true;
        if (responseStartOptions is not null)
        {
            responseStartOptions.OnResponseStarting(
                firstMessage ?? throw new InvalidOperationException("A JSON-RPC message is required to start this response."));
        }
    }

    private void QueuePendingHttpSseItem(SseItem<JsonRpcMessage?> item)
        => (_pendingHttpSseItems ??= []).Add(item);

    private async ValueTask WritePendingHttpSseItemsAsync(CancellationToken cancellationToken)
    {
        if (_pendingHttpSseItems is not { } pendingItems)
        {
            return;
        }

        _pendingHttpSseItems = null;
        foreach (var item in pendingItems)
        {
            await _httpSseWriter.WriteAsync(item, cancellationToken).ConfigureAwait(false);
        }
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

            if (_storeSseWriter is not null)
            {
                item = await _storeSseWriter.WriteEventAsync(item, cancellationToken).ConfigureAwait(false);
            }

            if (!_httpResponseCompleted)
            {
                // Only write the message to the response if the response has not completed.

                try
                {
                    StartHttpResponse(message);
                    await WritePendingHttpSseItemsAsync(cancellationToken).ConfigureAwait(false);
                    await _httpSseWriter.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _httpResponseTcs.TrySetException(ex);
                }
            }
        }
        finally
        {
            // Complete the response if this is the final message.
            if ((message is JsonRpcResponse or JsonRpcError) && ((JsonRpcMessageWithId)message).Id == _pendingRequest)
            {
                _finalResponseMessageSent = true;
                _httpResponseTcs.TrySetResult(true);
                _storeStreamTcs?.TrySetResult(true);
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

        if (_storeSseWriter is null)
        {
            throw new InvalidOperationException($"Polling requires an event stream store to be configured.");
        }

        // Send the priming event with the new retry interval.
        var primingItem = await _storeSseWriter.WriteEventAsync(
            sseItem: new SseItem<JsonRpcMessage?>() { ReconnectionInterval = retryInterval },
            cancellationToken)
            .ConfigureAwait(false);

        // Write to the response stream if it still exists.
        if (!_httpResponseCompleted)
        {
            if (responseStartOptions is not null && !_httpResponseStarted)
            {
                QueuePendingHttpSseItem(primingItem);
            }
            else
            {
                StartHttpResponse(firstMessage: null);
                await _httpSseWriter.WriteAsync(primingItem, cancellationToken).ConfigureAwait(false);
            }
        }

        // Set the mode to 'Polling' so that the replay stream ends as soon as all available messages have been sent.
        // This prevents the client from immediately establishing another long-lived connection.
#pragma warning disable MCP9006 // Stateful Streamable HTTP resumability types are obsolete but still wired up internally.
        await _storeSseWriter.SetModeAsync(SseEventStreamMode.Polling, cancellationToken).ConfigureAwait(false);
#pragma warning restore MCP9006

        // A response waiting for its first JSON-RPC message cannot complete until that message has
        // started the response and flushed any queued priming events.
        if (responseStartOptions is null || _httpResponseStarted)
        {
            _httpResponseTcs.TrySetResult(true);
        }
    }

    private async ValueTask<SseItem<JsonRpcMessage?>?> TryStartSseEventStreamAsync(RequestId requestId)
    {
        Debug.Assert(_storeSseWriter is null);

        _storeSseWriter = await parentTransport.TryCreateEventStreamAsync(
            streamId: requestId.Id!.ToString()!,
            cancellationToken: sessionCancellationToken)
            .ConfigureAwait(false);

        if (_storeSseWriter is null)
        {
            return null;
        }

        _storeStreamTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = HandleStoreStreamDisposalAsync(_storeStreamTcs.Task);

        return await _storeSseWriter.WriteEventAsync(SseItem.Prime<JsonRpcMessage>(), sessionCancellationToken).ConfigureAwait(false);

        async Task HandleStoreStreamDisposalAsync(Task streamTask)
        {
            try
            {
                await streamTask.WaitAsync(sessionCancellationToken).ConfigureAwait(false);
            }
            finally
            {
                using var _ = await _messageLock.LockAsync().ConfigureAwait(false);

                try
                {
                    await _storeSseWriter!.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogStoreStreamDisposalFailed(ex);
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        using var _ = await _messageLock.LockAsync().ConfigureAwait(false);

        if (_httpResponseCompleted)
        {
            return;
        }

        _httpResponseCompleted = true;

        _httpResponseTcs.TrySetResult(true);

        _httpSseWriter.Dispose();

        // Don't dispose the event stream writer here, as we may continue to write to the event store
        // after disposal if there are pending messages.
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to dispose SSE event stream writer.")]
    private partial void LogStoreStreamDisposalFailed(Exception exception);
}
