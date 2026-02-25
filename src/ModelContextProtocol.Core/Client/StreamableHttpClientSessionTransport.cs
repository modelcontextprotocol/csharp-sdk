using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using System.Threading.Channels;
using System.Net;

namespace ModelContextProtocol.Client;

/// <summary>
/// The Streamable HTTP client transport implementation
/// </summary>
internal sealed partial class StreamableHttpClientSessionTransport : TransportBase
{
    private static readonly MediaTypeWithQualityHeaderValue s_applicationJsonMediaType = new("application/json");
    private static readonly MediaTypeWithQualityHeaderValue s_textEventStreamMediaType = new("text/event-stream");

    private readonly McpHttpClient _httpClient;
    private readonly HttpClientTransportOptions _options;
    private readonly CancellationTokenSource _connectionCts;
    private readonly ILogger _logger;

    private string? _negotiatedProtocolVersion;
    private Task? _getReceiveTask;

    private readonly SemaphoreSlim _disposeLock = new(1, 1);
    private bool _disposed;

    public StreamableHttpClientSessionTransport(
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
        _httpClient = httpClient;
        _connectionCts = new CancellationTokenSource();
        _logger = (ILogger?)loggerFactory?.CreateLogger<HttpClientTransport>() ?? NullLogger.Instance;

        // We connect with the initialization request with the MCP transport. This means that any errors won't be observed
        // until the first call to SendMessageAsync. Fortunately, that happens internally in McpClient.ConnectAsync
        // so we still throw any connection-related Exceptions from there and never expose a pre-connected client to the user.
        SetConnected();

        if (_options.KnownSessionId is { } knownSessionId)
        {
            SessionId = knownSessionId;
            _getReceiveTask = ReceiveUnsolicitedMessagesAsync();
        }
    }

    /// <inheritdoc/>
    public override async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        // Immediately dispose the response. SendHttpRequestAsync only returns the response so the auto transport can look at it.
        using var response = await SendHttpRequestAsync(message, cancellationToken).ConfigureAwait(false);
        await response.EnsureSuccessStatusCodeWithResponseBodyAsync(cancellationToken).ConfigureAwait(false);
    }

    // This is used by the auto transport so it can fall back and try SSE given a non-200 response without catching an exception.
    internal async Task<HttpResponseMessage> SendHttpRequestAsync(JsonRpcMessage message, CancellationToken cancellationToken)
    {
        if (_options.KnownSessionId is not null &&
            message is JsonRpcRequest { Method: RequestMethods.Initialize })
        {
            throw new InvalidOperationException(
                $"Cannot send '{RequestMethods.Initialize}' when {nameof(HttpClientTransportOptions)}.{nameof(HttpClientTransportOptions.KnownSessionId)} is configured. " +
                $"Call {nameof(McpClient)}.{nameof(McpClient.ResumeSessionAsync)} to resume existing sessions.");
        }

        LogTransportSendingMessageSensitive(message);

        using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _connectionCts.Token);
        cancellationToken = sendCts.Token;

        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Headers =
            {
                Accept = { s_applicationJsonMediaType, s_textEventStreamMediaType },
            },
        };

        CopyAdditionalHeaders(httpRequestMessage.Headers, _options.AdditionalHeaders, SessionId, _negotiatedProtocolVersion);

        var response = await _httpClient.SendAsync(httpRequestMessage, message, cancellationToken).ConfigureAwait(false);

        // We'll let the caller decide whether to throw or fall back given an unsuccessful response.
        if (!response.IsSuccessStatusCode)
        {
            return response;
        }

        var rpcRequest = message as JsonRpcRequest;
        JsonRpcMessageWithId? rpcResponseOrError = null;

        if (response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            rpcResponseOrError = await ProcessMessageAsync(responseContent, rpcRequest, cancellationToken).ConfigureAwait(false);
        }
        else if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            var sseState = new SseStreamState();
            using var responseBodyStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var sseResponse = await ProcessSseResponseAsync(responseBodyStream, rpcRequest, sseState, cancellationToken).ConfigureAwait(false);
            rpcResponseOrError = sseResponse.Response;

            // Resumability: If POST SSE stream ended without a response but we have a Last-Event-ID (from priming),
            // attempt to resume by sending a GET request with Last-Event-ID header. The server will replay
            // events from the event store, allowing us to receive the pending response.
            if (rpcResponseOrError is null && rpcRequest is not null && sseState.LastEventId is not null)
            {
                rpcResponseOrError = await SendGetSseRequestWithRetriesAsync(rpcRequest, sseState, cancellationToken).ConfigureAwait(false);
            }
        }

        if (rpcRequest is null)
        {
            return response;
        }

        if (rpcResponseOrError is null)
        {
            throw new McpException($"Streamable HTTP POST response completed without a reply to request with ID: {rpcRequest.Id}");
        }

        if (rpcRequest.Method == RequestMethods.Initialize && rpcResponseOrError is JsonRpcResponse initResponse)
        {
            // We've successfully initialized! Copy session-id and protocol version, then start GET request if any.
            if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionIdValues))
            {
                SessionId = sessionIdValues.FirstOrDefault();
            }

            var initializeResult = JsonSerializer.Deserialize(initResponse.Result, McpJsonUtilities.JsonContext.Default.InitializeResult);
            _negotiatedProtocolVersion = initializeResult?.ProtocolVersion;

            _getReceiveTask ??= ReceiveUnsolicitedMessagesAsync();
        }

        return response;
    }

    public override async ValueTask DisposeAsync()
    {
        using var _ = await _disposeLock.LockAsync().ConfigureAwait(false);

        if (_disposed)
        {
            return;
        }
        _disposed = true;

        try
        {
            await _connectionCts.CancelAsync().ConfigureAwait(false);

            try
            {
                // Send DELETE request to terminate the session. Only send if we have a session ID, per MCP spec.
                if (_options.OwnsSession && !string.IsNullOrEmpty(SessionId))
                {
                    await SendDeleteRequest().ConfigureAwait(false);
                }

                if (_getReceiveTask != null)
                {
                    await _getReceiveTask.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogTransportShutdownFailed(Name, ex);
            }
            finally
            {
                _connectionCts.Dispose();
            }
        }
        finally
        {
            // If we're auto-detecting the transport and failed to connect, leave the message Channel open for the SSE transport.
            // This class isn't directly exposed to public callers, so we don't have to worry about changing the _state in this case.
            if (_options.TransportMode is not HttpTransportMode.AutoDetect || _getReceiveTask is not null)
            {
                SetDisconnected();
            }
        }
    }

    private async Task ReceiveUnsolicitedMessagesAsync()
    {
        var state = new SseStreamState();

        // Continuously receive unsolicited messages until canceled
        while (!_connectionCts.Token.IsCancellationRequested)
        {
            await SendGetSseRequestWithRetriesAsync(
                relatedRpcRequest: null,
                state,
                _connectionCts.Token).ConfigureAwait(false);

            // If we exhausted retries without receiving any events, stop trying
            if (state.LastEventId is null)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Sends a GET request for SSE with retry logic and resumability support.
    /// </summary>
    private async Task<JsonRpcMessageWithId?> SendGetSseRequestWithRetriesAsync(
        JsonRpcRequest? relatedRpcRequest,
        SseStreamState state,
        CancellationToken cancellationToken)
    {
        // When LastEventId is null, the first attempt is the initial GET SSE connection (not a reconnection),
        // so we start at -1 to avoid counting it against MaxReconnectionAttempts.
        // When LastEventId is already set, all attempts are true reconnections, so we start at 0.
        int attempt = state.LastEventId is null ? -1 : 0;

        // Delay before first attempt if we're reconnecting (have a Last-Event-ID)
        bool shouldDelay = state.LastEventId is not null;

        while (attempt < _options.MaxReconnectionAttempts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (shouldDelay)
            {
                var delay = state.RetryInterval ?? _options.DefaultReconnectionInterval;

                // Subtract time already elapsed since the SSE stream ended to more accurately
                // honor the retry interval. Without this, processing overhead (HTTP response
                // disposal, condition checks, etc.) inflates the observed reconnection delay.
                if (state.StreamEndedTimestamp != 0)
                {
                    delay -= ElapsedSince(state.StreamEndedTimestamp);
                }

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
            shouldDelay = true;

            using var request = new HttpRequestMessage(HttpMethod.Get, _options.Endpoint);
            request.Headers.Accept.Add(s_textEventStreamMediaType);
            CopyAdditionalHeaders(request.Headers, _options.AdditionalHeaders, SessionId, _negotiatedProtocolVersion, state.LastEventId);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, message: null, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                attempt++;
                continue;
            }

            using (response)
            {
                if (response.StatusCode >= HttpStatusCode.InternalServerError)
                {
                    // Server error; retry.
                    attempt++;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    // If the server could be reached but returned a non-success status code,
                    // retrying likely won't change that.
                    return null;
                }

                using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var sseResponse = await ProcessSseResponseAsync(responseStream, relatedRpcRequest, state, cancellationToken).ConfigureAwait(false);

                if (sseResponse.Response is { } rpcResponseOrError)
                {
                    return rpcResponseOrError;
                }

                // If we reach here, then the stream closed without the response.

                if (sseResponse.IsNetworkError || state.LastEventId is null)
                {
                    // No event ID means server may not support resumability; don't retry indefinitely.
                    attempt++;
                }
                else
                {
                    // We have an event ID, so we continue polling to receive more events.
                    // The server should eventually send a response or return an error.
                    attempt = 0;
                }
            }
        }

        return null;
    }

    private async Task<SseResponse> ProcessSseResponseAsync(
        Stream responseStream,
        JsonRpcRequest? relatedRpcRequest,
        SseStreamState state,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (SseItem<string> sseEvent in SseParser.Create(responseStream).EnumerateAsync(cancellationToken).ConfigureAwait(false))
            {
                // Track event ID and retry interval for resumability
                if (!string.IsNullOrEmpty(sseEvent.EventId))
                {
                    state.LastEventId = sseEvent.EventId;
                }
                if (sseEvent.ReconnectionInterval.HasValue)
                {
                    state.RetryInterval = sseEvent.ReconnectionInterval.Value;
                }

                // Skip events with empty data
                if (string.IsNullOrEmpty(sseEvent.Data))
                {
                    continue;
                }

                var rpcResponseOrError = await ProcessMessageAsync(sseEvent.Data, relatedRpcRequest, cancellationToken).ConfigureAwait(false);
                if (rpcResponseOrError is not null)
                {
                    return new() { Response = rpcResponseOrError };
                }
            }
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            state.StreamEndedTimestamp = Stopwatch.GetTimestamp();
            return new() { IsNetworkError = true };
        }

        state.StreamEndedTimestamp = Stopwatch.GetTimestamp();
        return default;
    }

    private async Task<JsonRpcMessageWithId?> ProcessMessageAsync(string data, JsonRpcRequest? relatedRpcRequest, CancellationToken cancellationToken)
    {
        LogTransportReceivedMessageSensitive(Name, data);

        try
        {
            var message = JsonSerializer.Deserialize(data, McpJsonUtilities.JsonContext.Default.JsonRpcMessage);
            if (message is null)
            {
                LogTransportMessageParseUnexpectedTypeSensitive(Name, data);
                return null;
            }

            await WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);
            if (message is JsonRpcResponse or JsonRpcError &&
                message is JsonRpcMessageWithId rpcResponseOrError &&
                rpcResponseOrError.Id == relatedRpcRequest?.Id)
            {
                return rpcResponseOrError;
            }
        }
        catch (JsonException ex)
        {
            LogJsonException(ex, data);
        }

        return null;
    }

    private async Task SendDeleteRequest()
    {
        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, _options.Endpoint);
        CopyAdditionalHeaders(deleteRequest.Headers, _options.AdditionalHeaders, SessionId, _negotiatedProtocolVersion);

        // Do not validate we get a successful status code, because server support for the DELETE request is optional
        (await _httpClient.SendAsync(deleteRequest, message: null, CancellationToken.None).ConfigureAwait(false)).Dispose();
    }

    private void LogJsonException(JsonException ex, string data)
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

    internal static void CopyAdditionalHeaders(
        HttpRequestHeaders headers,
        IDictionary<string, string>? additionalHeaders,
        string? sessionId,
        string? protocolVersion,
        string? lastEventId = null)
    {
        if (sessionId is not null)
        {
            headers.Add("Mcp-Session-Id", sessionId);
        }

        if (protocolVersion is not null)
        {
            headers.Add("MCP-Protocol-Version", protocolVersion);
        }

        if (lastEventId is not null)
        {
            headers.Add("Last-Event-ID", lastEventId);
        }

        if (additionalHeaders is null)
        {
            return;
        }

        foreach (var header in additionalHeaders)
        {
            if (!headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                throw new InvalidOperationException($"Failed to add header '{header.Key}' with value '{header.Value}' from {nameof(HttpClientTransportOptions.AdditionalHeaders)}.");
            }
        }
    }

    /// <summary>
    /// Tracks state across SSE stream connections.
    /// </summary>
    private sealed class SseStreamState
    {
        public string? LastEventId { get; set; }
        public TimeSpan? RetryInterval { get; set; }
        /// <summary>Timestamp (via Stopwatch.GetTimestamp()) when the last SSE stream ended, used to discount processing overhead from the retry delay.</summary>
        public long StreamEndedTimestamp { get; set; }
    }

    /// <summary>
    /// Represents the result of processing an SSE response.
    /// </summary>
    private readonly struct SseResponse
    {
        public JsonRpcMessageWithId? Response { get; init; }
        public bool IsNetworkError { get; init; }
    }

    private static TimeSpan ElapsedSince(long stopwatchTimestamp)
    {
#if NET
        return Stopwatch.GetElapsedTime(stopwatchTimestamp);
#else
        return TimeSpan.FromSeconds((double)(Stopwatch.GetTimestamp() - stopwatchTimestamp) / Stopwatch.Frequency);
#endif
    }
}
