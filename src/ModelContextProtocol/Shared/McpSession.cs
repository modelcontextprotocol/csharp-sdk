using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.Collections.Concurrent;
using System.Text.Json;

namespace ModelContextProtocol.Shared;

/// <summary>
/// Class for managing an MCP JSON-RPC session. This covers both MCP clients and servers.
/// </summary>
internal sealed class McpSession : IDisposable
{
    private readonly ITransport _transport;
    private readonly RequestHandlers _requestHandlers;
    private readonly NotificationHandlers _notificationHandlers;

    /// <summary>Collection of requests sent on this session and waiting for responses.</summary>
    private readonly ConcurrentDictionary<RequestId, TaskCompletionSource<IJsonRpcMessage>> _pendingRequests = [];
    /// <summary>
    /// Collection of requests received on this session and currently being handled. The value provides a <see cref="CancellationTokenSource"/>
    /// that can be used to request cancellation of the in-flight handler.
    /// </summary>
    private readonly ConcurrentDictionary<RequestId, CancellationTokenSource> _handlingRequests = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger _logger;
    
    private readonly string _id = Guid.NewGuid().ToString("N");
    private long _nextRequestId;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpSession"/> class.
    /// </summary>
    /// <param name="transport">An MCP transport implementation.</param>
    /// <param name="endpointName">The name of the endpoint for logging and debug purposes.</param>
    /// <param name="requestHandlers">A collection of request handlers.</param>
    /// <param name="notificationHandlers">A collection of notification handlers.</param>
    /// <param name="logger">The logger.</param>
    public McpSession(
        ITransport transport,
        string endpointName,
        RequestHandlers requestHandlers,
        NotificationHandlers notificationHandlers,
        ILogger logger)
    {
        Throw.IfNull(transport);

        _transport = transport;
        EndpointName = endpointName;
        _requestHandlers = requestHandlers;
        _notificationHandlers = notificationHandlers;
        _jsonOptions = McpJsonUtilities.DefaultOptions;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Gets and sets the name of the endpoint for logging and debug purposes.
    /// </summary>
    public string EndpointName { get; set; }

    /// <summary>
    /// Starts processing messages from the transport. This method will block until the transport is disconnected.
    /// This is generally started in a background task or thread from the initialization logic of the derived class.
    /// </summary>
    public async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _transport.MessageReader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                _logger.TransportMessageRead(EndpointName, message.GetType().Name);

                _ = ProcessMessageAsync();
                async Task ProcessMessageAsync()
                {
                    IJsonRpcMessageWithId? messageWithId = message as IJsonRpcMessageWithId;
                    CancellationTokenSource? combinedCts = null;
                    try
                    {
                        // Register before we yield, so that the tracking is guaranteed to be there
                        // when subsequent messages arrive, even if the asynchronous processing happens
                        // out of order.
                        if (messageWithId is not null)
                        {
                            combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            _handlingRequests[messageWithId.Id] = combinedCts;
                        }

                        // Fire and forget the message handling to avoid blocking the transport
                        // If awaiting the task, the transport will not be able to read more messages,
                        // which could lead to a deadlock if the handler sends a message back

#if NET
                        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
#else
                        await default(ForceYielding);
#endif

                        // Handle the message.
                        await HandleMessageAsync(message, combinedCts?.Token ?? cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // Only send responses for request errors that aren't user-initiated cancellation.
                        bool isUserCancellation =
                            ex is OperationCanceledException &&
                            !cancellationToken.IsCancellationRequested &&
                            combinedCts?.IsCancellationRequested is true;

                        if (!isUserCancellation && message is JsonRpcRequest request)
                        {
                            _logger.RequestHandlerError(EndpointName, request.Method, ex);
                            await _transport.SendMessageAsync(new JsonRpcError
                            {
                                Id = request.Id,
                                JsonRpc = "2.0",
                                Error = new JsonRpcErrorDetail
                                {
                                    Code = ErrorCodes.InternalError,
                                    Message = ex.Message
                                }
                            }, cancellationToken).ConfigureAwait(false);
                        }
                        else if (ex is not OperationCanceledException)
                        {
                            var payload = JsonSerializer.Serialize(message, _jsonOptions.GetTypeInfo<IJsonRpcMessage>());
                            _logger.MessageHandlerError(EndpointName, message.GetType().Name, payload, ex);
                        }
                    }
                    finally
                    {
                        if (messageWithId is not null)
                        {
                            _handlingRequests.TryRemove(messageWithId.Id, out _);
                            combinedCts!.Dispose();
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown
            _logger.EndpointMessageProcessingCancelled(EndpointName);
        }
    }

    private async Task HandleMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken)
    {
        switch (message)
        {
            case JsonRpcRequest request:
                await HandleRequest(request, cancellationToken).ConfigureAwait(false);
                break;

            case IJsonRpcMessageWithId messageWithId:
                HandleMessageWithId(message, messageWithId);
                break;

            case JsonRpcNotification notification:
                await HandleNotification(notification).ConfigureAwait(false);
                break;

            default:
                _logger.EndpointHandlerUnexpectedMessageType(EndpointName, message.GetType().Name);
                break;
        }
    }

    private async Task HandleNotification(JsonRpcNotification notification)
    {
        // Special-case cancellation to cancel a pending operation. (We'll still subsequently invoke a user-specified handler if one exists.)
        if (notification.Method == NotificationMethods.CancelledNotification)
        {
            try
            {
                if (GetCancelledNotificationParams(notification.Params) is CancelledNotification cn &&
                    _handlingRequests.TryGetValue(cn.RequestId, out var cts))
                {
                    await cts.CancelAsync().ConfigureAwait(false);
                    _logger.RequestCanceled(cn.RequestId, cn.Reason);
                }
            }
            catch
            {
                // "Invalid cancellation notifications SHOULD be ignored"
            }
        }

        // Handle user-defined notifications.
        if (_notificationHandlers.TryGetValue(notification.Method, out var handlers))
        {
            foreach (var notificationHandler in handlers)
            {
                try
                {
                    await notificationHandler(notification).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Log handler error but continue processing
                    _logger.NotificationHandlerError(EndpointName, notification.Method, ex);
                }
            }
        }
    }

    private void HandleMessageWithId(IJsonRpcMessage message, IJsonRpcMessageWithId messageWithId)
    {
        if (messageWithId.Id.IsDefault)
        {
            _logger.RequestHasInvalidId(EndpointName);
        }
        else if (_pendingRequests.TryRemove(messageWithId.Id, out var tcs))
        {
            _logger.ResponseMatchedPendingRequest(EndpointName, messageWithId.Id.ToString());
            tcs.TrySetResult(message);
        }
        else
        {
            _logger.NoRequestFoundForMessageWithId(EndpointName, messageWithId.Id.ToString());
        }
    }

    private async Task HandleRequest(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        if (_requestHandlers.TryGetValue(request.Method, out var handler))
        {
            _logger.RequestHandlerCalled(EndpointName, request.Method);
            var result = await handler(request, cancellationToken).ConfigureAwait(false);
            _logger.RequestHandlerCompleted(EndpointName, request.Method);
            await _transport.SendMessageAsync(new JsonRpcResponse
            {
                Id = request.Id,
                JsonRpc = "2.0",
                Result = result
            }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.NoHandlerFoundForRequest(EndpointName, request.Method);
        }
    }

    /// <summary>
    /// Sends a generic JSON-RPC request to the server.
    /// It is strongly recommended use the capability-specific methods instead of this one.
    /// Use this method for custom requests or those not yet covered explicitly by the endpoint implementation.
    /// </summary>
    /// <typeparam name="TResult">The expected response type.</typeparam>
    /// <param name="request">The JSON-RPC request to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task containing the server's response.</returns>
    public async Task<TResult> SendRequestAsync<TResult>(JsonRpcRequest request, CancellationToken cancellationToken) where TResult : class
    {
        if (!_transport.IsConnected)
        {
            _logger.EndpointNotConnected(EndpointName);
            throw new McpClientException("Transport is not connected");
        }

        // Set request ID
        if (request.Id.IsDefault)
        {
            request.Id = new RequestId($"{_id}-{Interlocked.Increment(ref _nextRequestId)}");
        }

        var tcs = new TaskCompletionSource<IJsonRpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[request.Id] = tcs;

        try
        {
            // Expensive logging, use the logging framework to check if the logger is enabled
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.SendingRequestPayload(EndpointName, JsonSerializer.Serialize(request, _jsonOptions.GetTypeInfo<JsonRpcRequest>()));
            }

            // Less expensive information logging
            _logger.SendingRequest(EndpointName, request.Method);

            await _transport.SendMessageAsync(request, cancellationToken).ConfigureAwait(false);

            _logger.RequestSentAwaitingResponse(EndpointName, request.Method, request.Id.ToString());
            var response = await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            if (response is JsonRpcError error)
            {
                _logger.RequestFailed(EndpointName, request.Method, error.Error.Message, error.Error.Code);
                throw new McpClientException($"Request failed (server side): {error.Error.Message}", error.Error.Code);
            }

            if (response is JsonRpcResponse success)
            {
                // Convert the Result object to JSON and back to get our strongly-typed result
                var resultJson = JsonSerializer.Serialize(success.Result, _jsonOptions.GetTypeInfo<object?>());
                var resultObject = JsonSerializer.Deserialize(resultJson, _jsonOptions.GetTypeInfo<TResult>());

                // Not expensive logging because we're already converting to JSON in order to get the result object
                _logger.RequestResponseReceivedPayload(EndpointName, resultJson);
                _logger.RequestResponseReceived(EndpointName, request.Method);

                if (resultObject != null)
                {
                    return resultObject;
                }

                // Result object was null, this is unexpected
                _logger.RequestResponseTypeConversionError(EndpointName, request.Method, typeof(TResult));
                throw new McpClientException($"Unexpected response type {JsonSerializer.Serialize(success.Result, _jsonOptions.GetTypeInfo<TResult>())}, expected {typeof(TResult)}");
            }

            // Unexpected response type
            _logger.RequestInvalidResponseType(EndpointName, request.Method);
            throw new McpClientException("Invalid response type");
        }
        finally
        {
            _pendingRequests.TryRemove(request.Id, out _);
        }
    }

    public async Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(message);

        if (!_transport.IsConnected)
        {
            _logger.ClientNotConnected(EndpointName);
            throw new McpClientException("Transport is not connected");
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.SendingMessage(EndpointName, JsonSerializer.Serialize(message, _jsonOptions.GetTypeInfo<IJsonRpcMessage>()));
        }

        await _transport.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);

        // If the sent notification was a cancellation notification, cancel the pending request's await, as either the
        // server won't be sending a response, or per the specification, the response should be ignored. There are inherent
        // race conditions here, so it's possible and allowed for the operation to complete before we get to this point.
        if (message is JsonRpcNotification { Method: NotificationMethods.CancelledNotification } notification &&
            GetCancelledNotificationParams(notification.Params) is CancelledNotification cn &&
            _pendingRequests.TryRemove(cn.RequestId, out var tcs))
        {
            tcs.TrySetCanceled(default);
        }
    }

    private static CancelledNotification? GetCancelledNotificationParams(object? notificationParams)
    {
        try
        {
            switch (notificationParams)
            {
                case null:
                    return null;

                case CancelledNotification cn:
                    return cn;

                case JsonElement je:
                    return JsonSerializer.Deserialize(je, McpJsonUtilities.DefaultOptions.GetTypeInfo<CancelledNotification>());

                default:
                    return JsonSerializer.Deserialize(
                        JsonSerializer.Serialize(notificationParams, McpJsonUtilities.DefaultOptions.GetTypeInfo<object?>()),
                        McpJsonUtilities.DefaultOptions.GetTypeInfo<CancelledNotification>());
            }
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        // Complete all pending requests with cancellation
        foreach (var entry in _pendingRequests)
        {
            entry.Value.TrySetCanceled();
        }
        _pendingRequests.Clear();
    }
}
