using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Text.Json;
using System.Threading.Channels;

namespace ModelContextProtocol.Client;

/// <summary>
/// The ServerSideEvents client transport implementation
/// </summary>
internal sealed partial class SseClientSessionTransport : TransportBase
{
    private readonly McpHttpClient _httpClient;
    private readonly HttpClientTransportOptions _options;
    private readonly Uri _sseEndpoint;
    private Uri? _messageEndpoint;
    private readonly CancellationTokenSource _connectionCts;
    private Task? _receiveTask;
    private readonly ILogger _logger;
    private readonly TaskCompletionSource<bool> _connectionEstablished;
    private string? _lastEventId;
    private TimeSpan? _retryInterval;

    /// <summary>
    /// SSE transport for a single session. Unlike stdio it does not launch a process, but connects to an existing server.
    /// The HTTP server can be local or remote, and must support the SSE protocol.
    /// </summary>
    public SseClientSessionTransport(
        string endpointName,
        HttpClientTransportOptions transportOptions,
        McpHttpClient httpClient,
        Channel<JsonRpcMessage>? messageChannel,
        ILoggerFactory? loggerFactory)
        : base(endpointName, messageChannel, loggerFactory)
    {
        Throw.IfNull(transportOptions);
        Throw.IfNull(httpClient);

        _options = transportOptions;
        _sseEndpoint = transportOptions.Endpoint;
        _httpClient = httpClient;
        _connectionCts = new CancellationTokenSource();
        _logger = (ILogger?)loggerFactory?.CreateLogger<HttpClientTransport>() ?? NullLogger.Instance;
        _connectionEstablished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        Debug.Assert(!IsConnected);
        try
        {
            // Start message receiving loop
            _receiveTask = ReceiveMessagesAsync(_connectionCts.Token);

            await _connectionEstablished.Task.WaitAsync(_options.ConnectionTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogTransportConnectFailed(Name, ex);
            await CloseAsync().ConfigureAwait(false);
            throw new InvalidOperationException("Failed to connect transport", ex);
        }
    }

    /// <inheritdoc/>
    public override async Task SendMessageAsync(
        JsonRpcMessage message,
        CancellationToken cancellationToken = default)
    {
        if (_messageEndpoint == null)
            throw new InvalidOperationException("Transport not connected");

        string messageId = "(no id)";

        if (message is JsonRpcMessageWithId messageWithId)
        {
            messageId = messageWithId.Id.ToString();
        }

        LogTransportSendingMessageSensitive(message);

        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, _messageEndpoint);
        StreamableHttpClientSessionTransport.CopyAdditionalHeaders(httpRequestMessage.Headers, _options.AdditionalHeaders, sessionId: null, protocolVersion: null);
        var response = await _httpClient.SendAsync(httpRequestMessage, message, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                LogRejectedPostSensitive(Name, messageId, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            }
            else
            {
                LogRejectedPost(Name, messageId);
            }

            response.EnsureSuccessStatusCode();
        }
    }

    private async Task CloseAsync()
    {
        try
        {
            await _connectionCts.CancelAsync().ConfigureAwait(false);

            try
            {
                if (_receiveTask != null)
                {
                    await _receiveTask.ConfigureAwait(false);
                }
            }
            finally
            {
                _connectionCts.Dispose();
            }
        }
        finally
        {
            SetDisconnected();
        }
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        try
        {
            await CloseAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Ignore exceptions on close
        }
    }

    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
        int attempt = 0;

        while (attempt < _options.MaxReconnectionAttempts && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Delay before reconnection attempts.
                if (attempt > 0)
                {
                    await Task.Delay(_retryInterval ?? _options.DefaultReconnectionInterval, cancellationToken).ConfigureAwait(false);
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, _sseEndpoint);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                StreamableHttpClientSessionTransport.CopyAdditionalHeaders(request.Headers, _options.AdditionalHeaders, sessionId: null, protocolVersion: null);

                // Include Last-Event-ID header for reconnection.
                if (_lastEventId is not null)
                {
                    request.Headers.Add("Last-Event-ID", _lastEventId);
                    LogSseReconnectWithLastEventId(Name, _lastEventId);
                }

                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.SendAsync(request, message: null, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    // Network error - retry
                    attempt++;
                    continue;
                }

                using var _ = response;

                if (response.StatusCode >= HttpStatusCode.InternalServerError)
                {
                    // Server error - retry
                    attempt++;
                    continue;
                }

                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

                bool hadNetworkError = await ProcessSseStreamAsync(stream, cancellationToken).ConfigureAwait(false);

                if (!hadNetworkError || _lastEventId is null)
                {
                    // Stream ended either gracefully or without resumability support.
                    return;
                }

                // Only retry if the server didn't close gracefully and we have something to retry.
                attempt++;
                continue;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal shutdown via cancellation
                LogTransportReadMessagesCancelled(Name);
                _connectionEstablished.TrySetCanceled(cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                LogTransportReadMessagesFailed(Name, ex);
                _connectionEstablished.TrySetException(ex);
                throw;
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            LogTransportReadMessagesCancelled(Name);
            _connectionEstablished.TrySetCanceled(cancellationToken);
        }
        else
        {
            SetDisconnected();
        }
    }

    /// <summary>
    /// Processes the SSE stream, handling events until the stream ends.
    /// </summary>
    /// <returns>True if the stream ended due to a network error (should retry), false if it ended normally.</returns>
    private async Task<bool> ProcessSseStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (SseItem<string> sseEvent in SseParser.Create(stream).EnumerateAsync(cancellationToken).ConfigureAwait(false))
            {
                // Track event ID and retry interval for resumability
                var eventId = sseEvent.EventId;
                
                if (!string.IsNullOrEmpty(eventId))
                {
                    _lastEventId = eventId;
                    LogSseEventIdReceived(Name, eventId!);
                }

                if (sseEvent.ReconnectionInterval.HasValue)
                {
                    _retryInterval = sseEvent.ReconnectionInterval.Value;
                    LogSseRetryIntervalReceived(Name, sseEvent.ReconnectionInterval.Value.TotalMilliseconds);
                }

                switch (sseEvent.EventType)
                {
                    case "endpoint":
                        HandleEndpointEvent(sseEvent.Data);
                        break;

                    case "message":
                        await ProcessSseMessage(sseEvent.Data, cancellationToken).ConfigureAwait(false);
                        break;
                }
            }

            // Stream ended normally (server closed connection gracefully)
            return false;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            // Network error during streaming - should retry
            LogSseStreamNetworkError(Name, ex);
            return true;
        }
    }

    private async Task ProcessSseMessage(string data, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            LogTransportMessageReceivedBeforeConnected(Name);
            return;
        }

        LogTransportReceivedMessageSensitive(Name, data);

        try
        {
            var message = JsonSerializer.Deserialize(data, McpJsonUtilities.JsonContext.Default.JsonRpcMessage);
            if (message == null)
            {
                LogTransportMessageParseUnexpectedTypeSensitive(Name, data);
                return;
            }

            await WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                LogTransportMessageParseFailedSensitive(Name, data, ex);
            }
            else
            {
                LogTransportMessageParseFailed(Name, ex);
            }
        }
    }

    private void HandleEndpointEvent(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            LogTransportEndpointEventInvalid(Name);
            return;
        }

        // If data is an absolute URL, the Uri will be constructed entirely from it and not the _sseEndpoint.
        _messageEndpoint = new Uri(_sseEndpoint, data);

        // Set connected state
        SetConnected();
        _connectionEstablished.TrySetResult(true);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} accepted SSE transport POST for message ID '{MessageId}'.")]
    private partial void LogAcceptedPost(string endpointName, string messageId);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EndpointName} rejected SSE transport POST for message ID '{MessageId}'.")]
    private partial void LogRejectedPost(string endpointName, string messageId);

    [LoggerMessage(Level = LogLevel.Trace, Message = "{EndpointName} rejected SSE transport POST for message ID '{MessageId}'. Server response: '{responseContent}'.")]
    private partial void LogRejectedPostSensitive(string endpointName, string messageId, string responseContent);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{EndpointName} SSE reconnection with Last-Event-ID: '{LastEventId}'.")]
    private partial void LogSseReconnectWithLastEventId(string endpointName, string lastEventId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{EndpointName} SSE received event ID: '{EventId}'.")]
    private partial void LogSseEventIdReceived(string endpointName, string eventId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{EndpointName} SSE received retry interval: {RetryIntervalMs}ms.")]
    private partial void LogSseRetryIntervalReceived(string endpointName, double retryIntervalMs);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{EndpointName} SSE stream network error during streaming, will attempt reconnection.")]
    private partial void LogSseStreamNetworkError(string endpointName, Exception ex);
}