using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Shared;

/// <summary>
/// Class for managing an MCP JSON-RPC session. This covers both MCP clients and servers.
/// </summary>
internal sealed class McpSession : IDisposable
{
    private static readonly Histogram<double> s_clientSessionDuration = Diagnostics.CreateDurationHistogram(
        "mcp.client.session.duration", "Measures the duration of a client session.", longBuckets: true);
    private static readonly Histogram<double> s_serverSessionDuration = Diagnostics.CreateDurationHistogram(
        "mcp.server.session.duration", "Measures the duration of a server session.", longBuckets: true);
    private static readonly Histogram<double> s_clientOperationDuration = Diagnostics.CreateDurationHistogram(
        "mcp.client.operation.duration", "Measures the duration of outbound message.", longBuckets: false);
    private static readonly Histogram<double> s_serverOperationDuration = Diagnostics.CreateDurationHistogram(
        "mcp.server.operation.duration", "Measures the duration of inbound message processing.", longBuckets: false);

    private readonly bool _isServer;
    private readonly string _transportKind;
    private readonly ITransport _transport;
    private readonly RequestHandlers _requestHandlers;
    private readonly NotificationHandlers _notificationHandlers;
    private readonly long _sessionStartingTimestamp = Stopwatch.GetTimestamp();

    private readonly DistributedContextPropagator? _propagator = DistributedContextPropagator.Current;

    /// <summary>Collection of requests sent on this session and waiting for responses.</summary>
    private readonly ConcurrentDictionary<RequestId, TaskCompletionSource<IJsonRpcMessage>> _pendingRequests = [];
    /// <summary>
    /// Collection of requests received on this session and currently being handled. The value provides a <see cref="CancellationTokenSource"/>
    /// that can be used to request cancellation of the in-flight handler.
    /// </summary>
    private readonly ConcurrentDictionary<RequestId, CancellationTokenSource> _handlingRequests = new();
    private readonly ILogger _logger;

    private readonly string _id = Guid.NewGuid().ToString("N");
    private long _nextRequestId;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpSession"/> class.
    /// </summary>
    /// <param name="isServer">true if this is a server; false if it's a client.</param>
    /// <param name="transport">An MCP transport implementation.</param>
    /// <param name="endpointName">The name of the endpoint for logging and debug purposes.</param>
    /// <param name="requestHandlers">A collection of request handlers.</param>
    /// <param name="notificationHandlers">A collection of notification handlers.</param>
    /// <param name="logger">The logger.</param>
    public McpSession(
        bool isServer,
        ITransport transport,
        string endpointName,
        RequestHandlers requestHandlers,
        NotificationHandlers notificationHandlers,
        ILogger logger)
    {
        Throw.IfNull(transport);

        _transportKind = transport switch
        {
            StdioClientSessionTransport or StdioServerTransport => "stdio",
            StreamClientSessionTransport or StreamServerTransport => "stream",
            SseClientSessionTransport or SseResponseStreamTransport => "sse",
            _ => "unknownTransport"
        };

        _isServer = isServer;
        _transport = transport;
        EndpointName = endpointName;
        _requestHandlers = requestHandlers;
        _notificationHandlers = notificationHandlers;
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
                                    Code = (ex as McpException)?.ErrorCode ?? ErrorCodes.InternalError,
                                    Message = ex.Message
                                }
                            }, cancellationToken).ConfigureAwait(false);
                        }
                        else if (ex is not OperationCanceledException)
                        {
                            var payload = JsonSerializer.Serialize(message, McpJsonUtilities.JsonContext.Default.IJsonRpcMessage);
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
        finally
        {
            // Fail any pending requests, as they'll never be satisfied.
            foreach (var entry in _pendingRequests)
            {
                entry.Value.TrySetException(new InvalidOperationException("The server shut down unexpectedly."));
            }
        }
    }

    private async Task HandleMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken)
    {
        Histogram<double> durationMetric = _isServer ? s_serverOperationDuration : s_clientOperationDuration;
        string method = GetMethodName(message);

        long? startingTimestamp = durationMetric.Enabled ? Stopwatch.GetTimestamp() : null;

        Activity? activity = Diagnostics.ShouldInstrumentMessage(message)?
            Diagnostics.ActivitySource.StartActivity(
                CreateActivityName(method),
                ActivityKind.Server,
                parentContext: _propagator?.ExtractActivityContext(message) ?? default,
                links: Diagnostics.ActivityLinkFromCurrent()) :
            null;

        TagList tags = default;
        bool addTags = activity is { IsAllDataRequested: true } || startingTimestamp is not null;
        try
        {
            if (addTags)
            {
                AddTags(ref tags, activity, message, method);
            }

            switch (message)
            {
                case JsonRpcRequest request:
                    var result = await HandleRequest(request, cancellationToken).ConfigureAwait(false);
                    AddResponseTags(ref tags, activity, result, method);
                    break;

                case JsonRpcNotification notification:
                    await HandleNotification(notification, cancellationToken).ConfigureAwait(false);
                    break;

                case IJsonRpcMessageWithId messageWithId:
                    HandleMessageWithId(message, messageWithId);
                    break;

                default:
                    _logger.EndpointHandlerUnexpectedMessageType(EndpointName, message.GetType().Name);
                    break;
            }
        }
        catch (Exception e) when (addTags)
        {
            AddExceptionTags(ref tags, activity, e);
            throw;
        }
        finally
        {
            FinalizeDiagnostics(activity, startingTimestamp, durationMetric, ref tags);
        }
    }

    private async Task HandleNotification(JsonRpcNotification notification, CancellationToken cancellationToken)
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
        await _notificationHandlers.InvokeHandlers(notification.Method, notification, cancellationToken).ConfigureAwait(false);
    }

    private void HandleMessageWithId(IJsonRpcMessage message, IJsonRpcMessageWithId messageWithId)
    {
        if (messageWithId.Id.Id is null)
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

    private async Task<JsonNode?> HandleRequest(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        if (!_requestHandlers.TryGetValue(request.Method, out var handler))
        {
            _logger.NoHandlerFoundForRequest(EndpointName, request.Method);
            throw new McpException("The method does not exist or is not available.", ErrorCodes.MethodNotFound);
        }

        _logger.RequestHandlerCalled(EndpointName, request.Method);
        JsonNode? result = await handler(request, cancellationToken).ConfigureAwait(false);
        _logger.RequestHandlerCompleted(EndpointName, request.Method);
        await _transport.SendMessageAsync(new JsonRpcResponse
        {
            Id = request.Id,
            JsonRpc = "2.0",
            Result = result
        }, cancellationToken).ConfigureAwait(false);

        return result;
    }

    private CancellationTokenRegistration RegisterCancellation(CancellationToken cancellationToken, RequestId requestId)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            return default;
        }

        return cancellationToken.Register(static objState =>
        {
            var state = (Tuple<McpSession, RequestId>)objState!;
            _ = state.Item1.SendMessageAsync(new JsonRpcNotification
            {
                Method = NotificationMethods.CancelledNotification,
                Params = JsonSerializer.SerializeToNode(new CancelledNotification { RequestId = state.Item2 }, McpJsonUtilities.JsonContext.Default.CancelledNotification)
            });
        }, Tuple.Create(this, requestId));
    }

    public IAsyncDisposable RegisterNotificationHandler(string method, Func<JsonRpcNotification, CancellationToken, ValueTask> handler)
    {
        Throw.IfNullOrWhiteSpace(method);
        Throw.IfNull(handler);

        return _notificationHandlers.Register(method, handler);
    }

    /// <summary>
    /// Sends a JSON-RPC request to the server.
    /// It is strongly recommended use the capability-specific methods instead of this one.
    /// Use this method for custom requests or those not yet covered explicitly by the endpoint implementation.
    /// </summary>
    /// <param name="request">The JSON-RPC request to send.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task containing the server's response.</returns>
    public async Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        if (!_transport.IsConnected)
        {
            _logger.EndpointNotConnected(EndpointName);
            throw new McpException("Transport is not connected");
        }

        cancellationToken.ThrowIfCancellationRequested();

        Histogram<double> durationMetric = _isServer ? s_serverOperationDuration : s_clientOperationDuration;
        string method = request.Method;

        long? startingTimestamp = durationMetric.Enabled ? Stopwatch.GetTimestamp() : null;
        using Activity? activity = Diagnostics.ShouldInstrumentMessage(request) ?
            Diagnostics.ActivitySource.StartActivity(CreateActivityName(method), ActivityKind.Client) :
            null;

        // Set request ID
        if (request.Id.Id is null)
        {
            request.Id = new RequestId($"{_id}-{Interlocked.Increment(ref _nextRequestId)}");
        }

        _propagator?.InjectActivityContext(activity, request);

        TagList tags = default;
        bool addTags = activity is { IsAllDataRequested: true } || startingTimestamp is not null;

        var tcs = new TaskCompletionSource<IJsonRpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[request.Id] = tcs;
        try
        {
            if (addTags)
            {
                AddTags(ref tags, activity, request, method);
            }

            // Expensive logging, use the logging framework to check if the logger is enabled
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.SendingRequestPayload(EndpointName, JsonSerializer.Serialize(request, McpJsonUtilities.JsonContext.Default.JsonRpcRequest));
            }

            // Less expensive information logging
            _logger.SendingRequest(EndpointName, request.Method);

            await _transport.SendMessageAsync(request, cancellationToken).ConfigureAwait(false);
            _logger.RequestSentAwaitingResponse(EndpointName, request.Method, request.Id.ToString());

            // Now that the request has been sent, register for cancellation. If we registered before,
            // a cancellation request could arrive before the server knew about that request ID, in which
            // case the server could ignore it.
            IJsonRpcMessage? response;
            using (var registration = RegisterCancellation(cancellationToken, request.Id))
            {
                response = await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            if (response is JsonRpcError error)
            {
                _logger.RequestFailed(EndpointName, request.Method, error.Error.Message, error.Error.Code);
                throw new McpException($"Request failed (server side): {error.Error.Message}", error.Error.Code);
            }

            if (response is JsonRpcResponse success)
            {
                if (addTags)
                {
                    AddResponseTags(ref tags, activity, success.Result, method);
                }

                _logger.RequestResponseReceivedPayload(EndpointName, success.Result?.ToJsonString() ?? "null");
                _logger.RequestResponseReceived(EndpointName, request.Method);
                return success;
            }

            // Unexpected response type
            _logger.RequestInvalidResponseType(EndpointName, request.Method);
            throw new McpException("Invalid response type");
        }
        catch (Exception ex) when (addTags)
        {
            AddExceptionTags(ref tags, activity, ex);
            throw;
        }
        finally
        {
            _pendingRequests.TryRemove(request.Id, out _);
            FinalizeDiagnostics(activity, startingTimestamp, durationMetric, ref tags);
        }
    }

    public async Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(message);

        if (!_transport.IsConnected)
        {
            _logger.ClientNotConnected(EndpointName);
            throw new McpException("Transport is not connected");
        }

        cancellationToken.ThrowIfCancellationRequested();

        Histogram<double> durationMetric = _isServer ? s_serverOperationDuration : s_clientOperationDuration;
        string method = GetMethodName(message);

        long? startingTimestamp = durationMetric.Enabled ? Stopwatch.GetTimestamp() : null;
        using Activity? activity = Diagnostics.ShouldInstrumentMessage(message) ?
            Diagnostics.ActivitySource.StartActivity(CreateActivityName(method), ActivityKind.Client) :
            null;

        TagList tags = default;
        bool addTags = activity is { IsAllDataRequested: true } || startingTimestamp is not null;

        // propagate trace context, noop if activity is null
        _propagator?.InjectActivityContext(activity, message);

        try
        {
            if (addTags)
            {
                AddTags(ref tags, activity, message, method);
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.SendingMessage(EndpointName, JsonSerializer.Serialize(message, McpJsonUtilities.JsonContext.Default.IJsonRpcMessage));
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
        catch (Exception ex) when (addTags)
        {
            AddExceptionTags(ref tags, activity, ex);
            throw;
        }
        finally
        {
            FinalizeDiagnostics(activity, startingTimestamp, durationMetric, ref tags);
        }
    }

    private static CancelledNotification? GetCancelledNotificationParams(JsonNode? notificationParams)
    {
        try
        {
            return JsonSerializer.Deserialize(notificationParams, McpJsonUtilities.JsonContext.Default.CancelledNotification);
        }
        catch
        {
            return null;
        }
    }

    private string CreateActivityName(string method) => method;

    private static string GetMethodName(IJsonRpcMessage message) =>
        message switch
        {
            JsonRpcRequest request => request.Method,
            JsonRpcNotification notification => notification.Method,
            not null => message.GetType().FullName ?? "unknownMethod",
            _ => "unknownMethod"
        };

    private void AddTags(ref TagList tags, Activity? activity, IJsonRpcMessage message, string method)
    {
        tags.Add("mcp.method.name", method);
        tags.Add("network.transport", _transportKind);

        // RPC convention also includes:
        // server.address, server.port, client.address, client.port, network.peer.address, network.peer.port, network.type

        if (activity is { IsAllDataRequested: true })
        {
            // session and request id have high cardinality, so not applying to metric tags
            activity.AddTag("mcp.session.id", _id);

            if (message is IJsonRpcMessageWithId withId)
            {
                activity.AddTag("mcp.request.id", withId.Id.Id?.ToString());
            }
        }

        JsonObject? paramsObj = message switch
        {
            JsonRpcRequest request => request.Params as JsonObject,
            JsonRpcNotification notification => notification.Params as JsonObject,
            _ => null
        };

        if (paramsObj == null)
        {
            return;
        }

        string? target = null;
        switch (method)
        {
            case RequestMethods.ToolsCall:
                string? toolName = GetStringProperty(paramsObj, "name");
                if (toolName is not null)
                {
                    tags.Add("mcp.tool.name", toolName);
                    target = toolName;
                }
                break;
            case RequestMethods.PromptsGet:
                string? promptName = GetStringProperty(paramsObj, "name");
                if (promptName is not null)
                {
                    tags.Add("mcp.prompt.name", promptName);
                    target = promptName;
                }
                break;

            case RequestMethods.ResourcesRead:
            case RequestMethods.ResourcesSubscribe:
            case RequestMethods.ResourcesUnsubscribe:
            case NotificationMethods.ResourceUpdatedNotification:
                string? resourceUri = GetStringProperty(paramsObj, "uri");
                if (resourceUri is not null)
                {
                    tags.Add("mcp.resource.uri", resourceUri);
                    target = resourceUri;
                }
                break;
        }

        if (activity is { IsAllDataRequested: true })
        {
            activity.DisplayName = target == null ? method : $"{method} {target}";
        }
    }

    private static void AddExceptionTags(ref TagList tags, Activity? activity, Exception e)
    {
        if (e is AggregateException ae && ae.InnerException is not null and not AggregateException)
        {
            e = ae.InnerException;
        }

        int? intErrorCode = (e as McpException)?.ErrorCode is int errorCode ? errorCode :
            e is JsonException ? ErrorCodes.ParseError : null;

        tags.Add("error.type", intErrorCode == null ? e.GetType().FullName : intErrorCode.ToString());
        if (intErrorCode is not null)
        {
            tags.Add("rpc.jsonrpc.error_code", intErrorCode.ToString());
        }

        if (activity is { IsAllDataRequested: true })
        {
            activity.SetStatus(ActivityStatusCode.Error, e.Message);
        }
    }

    private static void AddResponseTags(ref TagList tags, Activity? activity, JsonNode? response, string method)
    {
        if (response is JsonObject jsonObject
            && jsonObject.TryGetPropertyValue("isError", out var isError)
            && isError?.GetValueKind() == JsonValueKind.True)
        {
            if (activity is { IsAllDataRequested: true })
            {
                string? content = null;
                if (jsonObject.TryGetPropertyValue("content", out var prop) && prop!= null)
                {
                    content = prop.ToJsonString();
                }
                activity.SetStatus(ActivityStatusCode.Error, content);
            }

            tags.Add("error.type", method == RequestMethods.ToolsCall ? "tool_error" : "_OTHER");
        }
    }

    private static void FinalizeDiagnostics(
        Activity? activity, long? startingTimestamp, Histogram<double> durationMetric, ref TagList tags)
    {
        try
        {
            if (startingTimestamp is not null)
            {
                durationMetric.Record(GetElapsed(startingTimestamp.Value).TotalSeconds, tags);
            }

            if (activity is { IsAllDataRequested: true })
            {
                foreach (var tag in tags)
                {
                    activity.AddTag(tag.Key, tag.Value);
                }
            }
        }
        finally
        {
            activity?.Dispose();
        }
    }

    public void Dispose()
    {
        Histogram<double> durationMetric = _isServer ? s_serverSessionDuration : s_clientSessionDuration;
        if (durationMetric.Enabled)
        {
            TagList tags = default;
            tags.Add("network.transport", _transportKind);
            // TODO server.address, server.port, client.address, client.port, network.peer.address, network.peer.port, network.type
            durationMetric.Record(GetElapsed(_sessionStartingTimestamp).TotalSeconds, tags);
        }

        // Complete all pending requests with cancellation
        foreach (var entry in _pendingRequests)
        {
            entry.Value.TrySetCanceled();
        }

        _pendingRequests.Clear();
    }

#if !NET
    private static readonly double s_timestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;
#endif

    private static TimeSpan GetElapsed(long startingTimestamp) =>
#if NET
        Stopwatch.GetElapsedTime(startingTimestamp);
#else
        new((long)(s_timestampToTicks * (Stopwatch.GetTimestamp() - startingTimestamp)));
#endif

    private static string? GetStringProperty(JsonObject parameters, string propName)
    {
        if (parameters.TryGetPropertyValue(propName, out var prop) && prop?.GetValueKind() is JsonValueKind.String)
        {
            return prop.GetValue<string>();
        }

        return null;
    }
}
