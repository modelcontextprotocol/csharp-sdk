using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Nodes;
#if !NET
using System.Threading.Channels;
#endif

namespace ModelContextProtocol;

/// <summary>
/// Class for managing an MCP JSON-RPC session. This covers both MCP clients and servers.
/// </summary>
internal sealed partial class McpSessionHandler : IAsyncDisposable
{
    private static readonly Histogram<double> s_clientSessionDuration = Diagnostics.CreateDurationHistogram(
        "mcp.client.session.duration", "Measures the duration of a client session.", longBuckets: true);
    private static readonly Histogram<double> s_serverSessionDuration = Diagnostics.CreateDurationHistogram(
        "mcp.server.session.duration", "Measures the duration of a server session.", longBuckets: true);
    private static readonly Histogram<double> s_clientOperationDuration = Diagnostics.CreateDurationHistogram(
        "mcp.client.operation.duration", "Measures the duration of outbound message.", longBuckets: false);
    private static readonly Histogram<double> s_serverOperationDuration = Diagnostics.CreateDurationHistogram(
        "mcp.server.operation.duration", "Measures the duration of inbound message processing.", longBuckets: false);

    /// <summary>The latest version of the protocol supported by this implementation.</summary>
    internal const string LatestProtocolVersion = "2025-06-18";

    /// <summary>All protocol versions supported by this implementation.</summary>
    internal static readonly string[] SupportedProtocolVersions =
    [
        "2024-11-05",
        "2025-03-26",
        LatestProtocolVersion,
    ];

    private readonly bool _isServer;
    private readonly string _transportKind;
    private readonly ITransport _transport;
    private readonly RequestHandlers _requestHandlers;
    private readonly NotificationHandlers _notificationHandlers;
    private readonly long _sessionStartingTimestamp = Stopwatch.GetTimestamp();

    private readonly DistributedContextPropagator _propagator = DistributedContextPropagator.Current;

    /// <summary>Collection of requests sent on this session and waiting for responses.</summary>
    private readonly ConcurrentDictionary<RequestId, TaskCompletionSource<JsonRpcMessage>> _pendingRequests = [];
    /// <summary>
    /// Collection of requests received on this session and currently being handled. The value provides a <see cref="CancellationTokenSource"/>
    /// that can be used to request cancellation of the in-flight handler.
    /// </summary>
    private readonly ConcurrentDictionary<RequestId, CancellationTokenSource> _handlingRequests = new();
    private readonly ILogger _logger;

    // This _sessionId is solely used to identify the session in telemetry and logs.
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private long _lastRequestId;

    private CancellationTokenSource? _messageProcessingCts;
    private Task? _messageProcessingTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpSessionHandler"/> class.
    /// </summary>
    /// <param name="isServer">true if this is a server; false if it's a client.</param>
    /// <param name="transport">An MCP transport implementation.</param>
    /// <param name="endpointName">The name of the endpoint for logging and debug purposes.</param>
    /// <param name="requestHandlers">A collection of request handlers.</param>
    /// <param name="notificationHandlers">A collection of notification handlers.</param>
    /// <param name="logger">The logger.</param>
    public McpSessionHandler(
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
            StreamableHttpClientSessionTransport or StreamableHttpServerTransport or StreamableHttpPostTransport => "http",
            _ => "unknownTransport"
        };

        _isServer = isServer;
        _transport = transport;
        EndpointName = endpointName;
        _requestHandlers = requestHandlers;
        _notificationHandlers = notificationHandlers;
        _logger = logger ?? NullLogger.Instance;
        LogSessionCreated(EndpointName, _sessionId, _transportKind);
    }

    /// <summary>
    /// Gets and sets the name of the endpoint for logging and debug purposes.
    /// </summary>
    public string EndpointName { get; set; }

    /// <summary>
    /// Starts processing messages from the transport. This method will block until the transport is disconnected.
    /// This is generally started in a background task or thread from the initialization logic of the derived class.
    /// </summary>
    public Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        if (_messageProcessingTask is not null)
        {
            throw new InvalidOperationException("The message processing loop has already started.");
        }

        Debug.Assert(_messageProcessingCts is null);

        _messageProcessingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _messageProcessingTask = ProcessMessagesCoreAsync(_messageProcessingCts.Token);
        return _messageProcessingTask;
    }

    private async Task ProcessMessagesCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _transport.MessageReader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                LogMessageRead(EndpointName, message.GetType().Name);

                // Fire and forget the message handling to avoid blocking the transport.
                if (message.Context?.ExecutionContext is null)
                {
                    _ = ProcessMessageAsync();
                }
                else
                {
                    // Flow the execution context from the HTTP request corresponding to this message if provided.
                    ExecutionContext.Run(message.Context.ExecutionContext, _ => _ = ProcessMessageAsync(), null);
                }

                async Task ProcessMessageAsync()
                {
                    JsonRpcMessageWithId? messageWithId = message as JsonRpcMessageWithId;
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

                        // If we await the handler without yielding first, the transport may not be able to read more messages,
                        // which could lead to a deadlock if the handler sends a message back.
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
                            LogRequestHandlerException(EndpointName, request.Method, ex);

                            JsonRpcErrorDetail detail = ex is McpException mcpe ?
                                new()
                                {
                                    Code = (int)mcpe.ErrorCode,
                                    Message = mcpe.Message,
                                } :
                                new()
                                {
                                    Code = (int)McpErrorCode.InternalError,
                                    Message = "An error occurred.",
                                };

                            var errorMessage = new JsonRpcError
                            {
                                Id = request.Id,
                                JsonRpc = "2.0",
                                Error = detail,
                                Context = new JsonRpcMessageContext { RelatedTransport = request.Context?.RelatedTransport },
                            };
                            await SendMessageAsync(errorMessage, cancellationToken).ConfigureAwait(false);
                        }
                        else if (ex is not OperationCanceledException)
                        {
                            if (_logger.IsEnabled(LogLevel.Trace))
                            {
                                LogMessageHandlerExceptionSensitive(EndpointName, message.GetType().Name, JsonSerializer.Serialize(message, McpJsonUtilities.JsonContext.Default.JsonRpcMessage), ex);
                            }
                            else
                            {
                                LogMessageHandlerException(EndpointName, message.GetType().Name, ex);
                            }
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
            LogEndpointMessageProcessingCanceled(EndpointName);
        }
        finally
        {
            // Fail any pending requests, as they'll never be satisfied.
            foreach (var entry in _pendingRequests)
            {
                entry.Value.TrySetException(new IOException("The server shut down unexpectedly."));
            }
        }
    }

    private async Task HandleMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken)
    {
        Histogram<double> durationMetric = _isServer ? s_serverOperationDuration : s_clientOperationDuration;
        string method = GetMethodName(message);

        long? startingTimestamp = durationMetric.Enabled ? Stopwatch.GetTimestamp() : null;

        Activity? activity = Diagnostics.ShouldInstrumentMessage(message) ?
            Diagnostics.ActivitySource.StartActivity(
                CreateActivityName(method),
                ActivityKind.Server,
                parentContext: _propagator.ExtractActivityContext(message),
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

                case JsonRpcMessageWithId messageWithId:
                    HandleMessageWithId(message, messageWithId);
                    break;

                default:
                    LogEndpointHandlerUnexpectedMessageType(EndpointName, message.GetType().Name);
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
                if (GetCancelledNotificationParams(notification.Params) is CancelledNotificationParams cn &&
                    _handlingRequests.TryGetValue(cn.RequestId, out var cts))
                {
                    await cts.CancelAsync().ConfigureAwait(false);
                    LogRequestCanceled(EndpointName, cn.RequestId, cn.Reason);
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

    private void HandleMessageWithId(JsonRpcMessage message, JsonRpcMessageWithId messageWithId)
    {
        if (_pendingRequests.TryRemove(messageWithId.Id, out var tcs))
        {
            tcs.TrySetResult(message);
        }
        else
        {
            LogNoRequestFoundForMessageWithId(EndpointName, messageWithId.Id);
        }
    }

    private async Task<JsonNode?> HandleRequest(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        if (!_requestHandlers.TryGetValue(request.Method, out var handler))
        {
            LogNoHandlerFoundForRequest(EndpointName, request.Method);
            throw new McpException($"Method '{request.Method}' is not available.", McpErrorCode.MethodNotFound);
        }

        LogRequestHandlerCalled(EndpointName, request.Method);
        JsonNode? result = await handler(request, cancellationToken).ConfigureAwait(false);
        LogRequestHandlerCompleted(EndpointName, request.Method);

        await SendMessageAsync(new JsonRpcResponse
        {
            Id = request.Id,
            Result = result,
            Context = request.Context,
        }, cancellationToken).ConfigureAwait(false);

        return result;
    }

    private CancellationTokenRegistration RegisterCancellation(CancellationToken cancellationToken, JsonRpcRequest request)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            return default;
        }

        return cancellationToken.Register(static objState =>
        {
            var state = (Tuple<McpSessionHandler, JsonRpcRequest>)objState!;
            _ = state.Item1.SendMessageAsync(new JsonRpcNotification
            {
                Method = NotificationMethods.CancelledNotification,
                Params = JsonSerializer.SerializeToNode(new CancelledNotificationParams { RequestId = state.Item2.Id }, McpJsonUtilities.JsonContext.Default.CancelledNotificationParams),
                Context = new JsonRpcMessageContext { RelatedTransport = state.Item2.Context?.RelatedTransport },
            });
        }, Tuple.Create(this, request));
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
        Throw.IfNull(request);

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
            request = request.WithId(new RequestId(Interlocked.Increment(ref _lastRequestId)));
        }

        _propagator.InjectActivityContext(activity, request);

        TagList tags = default;
        bool addTags = activity is { IsAllDataRequested: true } || startingTimestamp is not null;

        var tcs = new TaskCompletionSource<JsonRpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[request.Id] = tcs;
        try
        {
            if (addTags)
            {
                AddTags(ref tags, activity, request, method);
            }

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                LogSendingRequestSensitive(EndpointName, request.Method, JsonSerializer.Serialize(request, McpJsonUtilities.JsonContext.Default.JsonRpcMessage));
            }
            else
            {
                LogSendingRequest(EndpointName, request.Method);
            }

            await SendToRelatedTransportAsync(request, cancellationToken).ConfigureAwait(false);

            // Now that the request has been sent, register for cancellation. If we registered before,
            // a cancellation request could arrive before the server knew about that request ID, in which
            // case the server could ignore it.
            LogRequestSentAwaitingResponse(EndpointName, request.Method, request.Id);
            JsonRpcMessage? response;
            using (var registration = RegisterCancellation(cancellationToken, request))
            {
                response = await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            if (response is JsonRpcError error)
            {
                LogSendingRequestFailed(EndpointName, request.Method, error.Error.Message, error.Error.Code);
                throw new McpException($"Request failed (remote): {error.Error.Message}", (McpErrorCode)error.Error.Code);
            }

            if (response is JsonRpcResponse success)
            {
                if (addTags)
                {
                    AddResponseTags(ref tags, activity, success.Result, method);
                }

                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    LogRequestResponseReceivedSensitive(EndpointName, request.Method, success.Result?.ToJsonString() ?? "null");
                }
                else
                {
                    LogRequestResponseReceived(EndpointName, request.Method);
                }

                return success;
            }

            // Unexpected response type
            LogSendingRequestInvalidResponseType(EndpointName, request.Method);
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

    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(message);

        cancellationToken.ThrowIfCancellationRequested();

        Histogram<double> durationMetric = _isServer ? s_serverOperationDuration : s_clientOperationDuration;
        string method = GetMethodName(message);

        long? startingTimestamp = durationMetric.Enabled ? Stopwatch.GetTimestamp() : null;
        using Activity? activity = Diagnostics.ShouldInstrumentMessage(message) ?
            Diagnostics.ActivitySource.StartActivity(CreateActivityName(method), ActivityKind.Client) :
            null;

        TagList tags = default;
        bool addTags = activity is { IsAllDataRequested: true } || startingTimestamp is not null;

        // propagate trace context
        _propagator?.InjectActivityContext(activity, message);

        try
        {
            if (addTags)
            {
                AddTags(ref tags, activity, message, method);
            }

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                LogSendingMessageSensitive(EndpointName, JsonSerializer.Serialize(message, McpJsonUtilities.JsonContext.Default.JsonRpcMessage));
            }
            else
            {
                LogSendingMessage(EndpointName);
            }

            await SendToRelatedTransportAsync(message, cancellationToken).ConfigureAwait(false);

            // If the sent notification was a cancellation notification, cancel the pending request's await, as either the
            // server won't be sending a response, or per the specification, the response should be ignored. There are inherent
            // race conditions here, so it's possible and allowed for the operation to complete before we get to this point.
            if (message is JsonRpcNotification { Method: NotificationMethods.CancelledNotification } notification &&
                GetCancelledNotificationParams(notification.Params) is CancelledNotificationParams cn &&
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

    // The JsonRpcMessage should be sent over the RelatedTransport if set. This is used to support the
    // Streamable HTTP transport where the specification states that the server SHOULD include JSON-RPC responses in
    // the HTTP response body for the POST request containing the corresponding JSON-RPC request.
    private Task SendToRelatedTransportAsync(JsonRpcMessage message, CancellationToken cancellationToken)
        => (message.Context?.RelatedTransport ?? _transport).SendMessageAsync(message, cancellationToken);

    private static CancelledNotificationParams? GetCancelledNotificationParams(JsonNode? notificationParams)
    {
        try
        {
            return JsonSerializer.Deserialize(notificationParams, McpJsonUtilities.JsonContext.Default.CancelledNotificationParams);
        }
        catch
        {
            return null;
        }
    }

    private string CreateActivityName(string method) => method;

    private static string GetMethodName(JsonRpcMessage message) =>
        message switch
        {
            JsonRpcRequest request => request.Method,
            JsonRpcNotification notification => notification.Method,
            _ => "unknownMethod"
        };

    private void AddTags(ref TagList tags, Activity? activity, JsonRpcMessage message, string method)
    {
        tags.Add("mcp.method.name", method);
        tags.Add("network.transport", _transportKind);

        // TODO: When using SSE transport, add:
        // - server.address and server.port on client spans and metrics
        // - client.address and client.port on server spans (not metrics because of cardinality) when using SSE transport
        if (activity is { IsAllDataRequested: true })
        {
            // session and request id have high cardinality, so not applying to metric tags
            activity.AddTag("mcp.session.id", _sessionId);

            if (message is JsonRpcMessageWithId withId)
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
            case RequestMethods.PromptsGet:
                target = GetStringProperty(paramsObj, "name");
                if (target is not null)
                {
                    tags.Add(method == RequestMethods.ToolsCall ? "mcp.tool.name" : "mcp.prompt.name", target);
                }
                break;

            case RequestMethods.ResourcesRead:
            case RequestMethods.ResourcesSubscribe:
            case RequestMethods.ResourcesUnsubscribe:
            case NotificationMethods.ResourceUpdatedNotification:
                target = GetStringProperty(paramsObj, "uri");
                if (target is not null)
                {
                    tags.Add("mcp.resource.uri", target);
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

        int? intErrorCode =
            (int?)((e as McpException)?.ErrorCode) is int errorCode ? errorCode :
            e is JsonException ? (int)McpErrorCode.ParseError :
            null;

        string? errorType = intErrorCode?.ToString() ?? e.GetType().FullName;
        tags.Add("error.type", errorType);
        if (intErrorCode is not null)
        {
            tags.Add("rpc.jsonrpc.error_code", errorType);
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
                if (jsonObject.TryGetPropertyValue("content", out var prop) && prop != null)
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

    public async ValueTask DisposeAsync()
    {
        Histogram<double> durationMetric = _isServer ? s_serverSessionDuration : s_clientSessionDuration;
        if (durationMetric.Enabled)
        {
            TagList tags = default;
            tags.Add("network.transport", _transportKind);

            // TODO: Add server.address and server.port on client-side when using SSE transport,
            // client.* attributes are not added to metrics because of cardinality
            durationMetric.Record(GetElapsed(_sessionStartingTimestamp).TotalSeconds, tags);
        }

        foreach (var entry in _pendingRequests)
        {
            entry.Value.TrySetCanceled();
        }

        _pendingRequests.Clear();

        if (_messageProcessingCts is not null)
        {
            await _messageProcessingCts.CancelAsync().ConfigureAwait(false);
        }

        if (_messageProcessingTask is not null)
        {
            try
            {
                await _messageProcessingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation
            }
        }

        LogSessionDisposed(EndpointName, _sessionId, _transportKind);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} message processing canceled.")]
    private partial void LogEndpointMessageProcessingCanceled(string endpointName);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} method '{Method}' request handler called.")]
    private partial void LogRequestHandlerCalled(string endpointName, string method);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} method '{Method}' request handler completed.")]
    private partial void LogRequestHandlerCompleted(string endpointName, string method);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{EndpointName} method '{Method}' request handler failed.")]
    private partial void LogRequestHandlerException(string endpointName, string method, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} received request for unknown request ID '{RequestId}'.")]
    private partial void LogNoRequestFoundForMessageWithId(string endpointName, RequestId requestId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{EndpointName} request failed for method '{Method}': {ErrorMessage} ({ErrorCode}).")]
    private partial void LogSendingRequestFailed(string endpointName, string method, string errorMessage, int errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{EndpointName} received invalid response for method '{Method}'.")]
    private partial void LogSendingRequestInvalidResponseType(string endpointName, string method);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{EndpointName} sending method '{Method}' request.")]
    private partial void LogSendingRequest(string endpointName, string method);

    [LoggerMessage(Level = LogLevel.Trace, Message = "{EndpointName} sending method '{Method}' request. Request: '{Request}'.")]
    private partial void LogSendingRequestSensitive(string endpointName, string method, string request);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} canceled request '{RequestId}' per client notification. Reason: '{Reason}'.")]
    private partial void LogRequestCanceled(string endpointName, RequestId requestId, string? reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{EndpointName} Request response received for method {method}")]
    private partial void LogRequestResponseReceived(string endpointName, string method);

    [LoggerMessage(Level = LogLevel.Trace, Message = "{EndpointName} Request response received for method {method}. Response: '{Response}'.")]
    private partial void LogRequestResponseReceivedSensitive(string endpointName, string method, string response);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{EndpointName} read {MessageType} message from channel.")]
    private partial void LogMessageRead(string endpointName, string messageType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{EndpointName} message handler {MessageType} failed.")]
    private partial void LogMessageHandlerException(string endpointName, string messageType, Exception exception);

    [LoggerMessage(Level = LogLevel.Trace, Message = "{EndpointName} message handler {MessageType} failed. Message: '{Message}'.")]
    private partial void LogMessageHandlerExceptionSensitive(string endpointName, string messageType, string message, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{EndpointName} received unexpected {MessageType} message type.")]
    private partial void LogEndpointHandlerUnexpectedMessageType(string endpointName, string messageType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{EndpointName} received request for method '{Method}', but no handler is available.")]
    private partial void LogNoHandlerFoundForRequest(string endpointName, string method);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{EndpointName} waiting for response to request '{RequestId}' for method '{Method}'.")]
    private partial void LogRequestSentAwaitingResponse(string endpointName, string method, RequestId requestId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{EndpointName} sending message.")]
    private partial void LogSendingMessage(string endpointName);

    [LoggerMessage(Level = LogLevel.Trace, Message = "{EndpointName} sending message. Message: '{Message}'.")]
    private partial void LogSendingMessageSensitive(string endpointName, string message);

    [LoggerMessage(Level = LogLevel.Trace, Message = "{EndpointName} session {SessionId} created with transport {TransportKind}")]
    private partial void LogSessionCreated(string endpointName, string sessionId, string transportKind);

    [LoggerMessage(Level = LogLevel.Trace, Message = "{EndpointName} session {SessionId} disposed with transport {TransportKind}")]
    private partial void LogSessionDisposed(string endpointName, string sessionId, string transportKind);
}
