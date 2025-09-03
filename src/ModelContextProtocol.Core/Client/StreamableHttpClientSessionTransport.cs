using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using System.Threading.Channels;

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
    }

    /// <inheritdoc/>
    public override async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        // Immediately dispose the response. SendHttpRequestAsync only returns the response so the auto transport can look at it.
        using var response = await SendHttpRequestAsync(message, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    // This is used by the auto transport so it can fall back and try SSE given a non-200 response without catching an exception.
    internal async Task<HttpResponseMessage> SendHttpRequestAsync(JsonRpcMessage message, CancellationToken cancellationToken)
    {
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
            using var responseBodyStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            rpcResponseOrError = await ProcessSseResponseAsync(responseBodyStream, rpcRequest, cancellationToken).ConfigureAwait(false);
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

            _getReceiveTask = ReceiveUnsolicitedMessagesAsync();
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
                if (!string.IsNullOrEmpty(SessionId))
                {
                    await SendDeleteRequest();
                }

                if (_getReceiveTask != null)
                {
                    await _getReceiveTask.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
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
        // Send a GET request to handle any unsolicited messages not sent over a POST response.
        using var request = new HttpRequestMessage(HttpMethod.Get, _options.Endpoint);
        request.Headers.Accept.Add(s_textEventStreamMediaType);
        CopyAdditionalHeaders(request.Headers, _options.AdditionalHeaders, SessionId, _negotiatedProtocolVersion);

        using var response = await _httpClient.SendAsync(request, message: null, _connectionCts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // Server support for the GET request is optional. If it fails, we don't care. It just means we won't receive unsolicited messages.
            return;
        }

        using var responseStream = await response.Content.ReadAsStreamAsync(_connectionCts.Token).ConfigureAwait(false);
        await ProcessSseResponseAsync(responseStream, relatedRpcRequest: null, _connectionCts.Token).ConfigureAwait(false);
    }

    private async Task<JsonRpcMessageWithId?> ProcessSseResponseAsync(Stream responseStream, JsonRpcRequest? relatedRpcRequest, CancellationToken cancellationToken)
    {
        await foreach (SseItem<string> sseEvent in SseParser.Create(responseStream).EnumerateAsync(cancellationToken).ConfigureAwait(false))
        {
            if (sseEvent.EventType != "message")
            {
                continue;
            }

            var rpcResponseOrError = await ProcessMessageAsync(sseEvent.Data, relatedRpcRequest, cancellationToken).ConfigureAwait(false);

            // The server SHOULD end the HTTP response body here anyway, but we won't leave it to chance. This transport makes
            // a GET request for any notifications that might need to be sent after the completion of each POST.
            if (rpcResponseOrError is not null)
            {
                return rpcResponseOrError;
            }
        }

        return null;
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

        try
        {
            // Do not validate we get a successful status code, because server support for the DELETE request is optional
            (await _httpClient.SendAsync(deleteRequest, message: null, CancellationToken.None).ConfigureAwait(false)).Dispose();
        }
        catch (Exception ex)
        {
            LogTransportShutdownFailed(Name, ex);
        }
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
        string? protocolVersion)
    {
        if (sessionId is not null)
        {
            headers.Add("Mcp-Session-Id", sessionId);
        }

        if (protocolVersion is not null)
        {
            headers.Add("MCP-Protocol-Version", protocolVersion);
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
}
