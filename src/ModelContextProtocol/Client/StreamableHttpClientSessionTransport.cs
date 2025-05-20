using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Text.Json;
using ModelContextProtocol.Protocol;
#if NET
using System.Net.Http.Json;
#else
using System.Text;
#endif

namespace ModelContextProtocol.Client;

/// <summary>
/// The Streamable HTTP client transport implementation
/// </summary>
internal sealed partial class StreamableHttpClientSessionTransport : TransportBase
{
    private static readonly MediaTypeWithQualityHeaderValue s_applicationJsonMediaType = new("application/json");
    private static readonly MediaTypeWithQualityHeaderValue s_textEventStreamMediaType = new("text/event-stream");

    private readonly HttpClient _httpClient;
    private readonly SseClientTransportOptions _options;
    private readonly CancellationTokenSource _connectionCts;
    private readonly ILogger _logger;

    private string? _mcpSessionId;
    private Task? _getReceiveTask;

    public StreamableHttpClientSessionTransport(SseClientTransportOptions transportOptions, HttpClient httpClient, ILoggerFactory? loggerFactory, string endpointName)
        : base(endpointName, loggerFactory)
    {
        Throw.IfNull(transportOptions);
        Throw.IfNull(httpClient);

        _options = transportOptions;
        _httpClient = httpClient;
        _connectionCts = new CancellationTokenSource();
        _logger = (ILogger?)loggerFactory?.CreateLogger<SseClientTransport>() ?? NullLogger.Instance;

        // We connect with the initialization request with the MCP transport. This means that any errors won't be observed
        // until the first call to SendMessageAsync. Fortunately, that happens internally in McpClientFactory.ConnectAsync
        // so we still throw any connection-related Exceptions from there and never expose a pre-connected client to the user.
        SetConnected();
    }

    /// <inheritdoc/>
    public override async Task SendMessageAsync(
        JsonRpcMessage message,
        CancellationToken cancellationToken = default)
    {
        using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _connectionCts.Token);
        cancellationToken = sendCts.Token;

#if NET
        using var content = JsonContent.Create(message, McpJsonUtilities.JsonContext.Default.JsonRpcMessage);
#else
        using var content = new StringContent(
            JsonSerializer.Serialize(message, McpJsonUtilities.JsonContext.Default.JsonRpcMessage),
            Encoding.UTF8,
            "application/json; charset=utf-8"
        );
#endif

        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Content = content,
            Headers =
            {
                Accept = { s_applicationJsonMediaType, s_textEventStreamMediaType },
            },
        };

        CopyAdditionalHeaders(httpRequestMessage.Headers, _options.AdditionalHeaders, _mcpSessionId);
        using var response = await _httpClient.SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var rpcRequest = message as JsonRpcRequest;
        JsonRpcMessage? rpcResponseCandidate = null;

        if (response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            rpcResponseCandidate = await ProcessMessageAsync(responseContent, cancellationToken).ConfigureAwait(false);
        }
        else if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            using var responseBodyStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            rpcResponseCandidate = await ProcessSseResponseAsync(responseBodyStream, rpcRequest, cancellationToken).ConfigureAwait(false);
        }

        if (rpcRequest is null)
        {
            return;
        }

        if (rpcResponseCandidate is not JsonRpcMessageWithId messageWithId || messageWithId.Id != rpcRequest.Id)
        {
            throw new McpException($"Streamable HTTP POST response completed without a reply to request with ID: {rpcRequest.Id}");
        }

        if (rpcRequest.Method == RequestMethods.Initialize && rpcResponseCandidate is JsonRpcResponse)
        {
            // We've successfully initialized! Copy session-id and start GET request if any.
            if (response.Headers.TryGetValues("mcp-session-id", out var sessionIdValues))
            {
                _mcpSessionId = sessionIdValues.FirstOrDefault();
            }

            _getReceiveTask = ReceiveUnsolicitedMessagesAsync();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            await _connectionCts.CancelAsync().ConfigureAwait(false);

            try
            {
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
            SetDisconnected();
        }
    }

    private async Task ReceiveUnsolicitedMessagesAsync()
    {
        // Send a GET request to handle any unsolicited messages not sent over a POST response.
        using var request = new HttpRequestMessage(HttpMethod.Get, _options.Endpoint);
        request.Headers.Accept.Add(s_textEventStreamMediaType);
        CopyAdditionalHeaders(request.Headers, _options.AdditionalHeaders, _mcpSessionId);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _connectionCts.Token).ConfigureAwait(false);

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

            var message = await ProcessMessageAsync(sseEvent.Data, cancellationToken).ConfigureAwait(false);

            // The server SHOULD end the response here anyway, but we won't leave it to chance. This transport makes
            // a GET request for any notifications that might need to be sent after the completion of each POST.
            if (message is JsonRpcMessageWithId messageWithId && relatedRpcRequest?.Id == messageWithId.Id)
            {
                return messageWithId;
            }
        }

        return null;
    }

    private async Task<JsonRpcMessage?> ProcessMessageAsync(string data, CancellationToken cancellationToken)
    {
        try
        {
            var message = JsonSerializer.Deserialize(data, McpJsonUtilities.JsonContext.Default.JsonRpcMessage);
            if (message is null)
            {
                LogTransportMessageParseUnexpectedTypeSensitive(Name, data);
                return null;
            }

            await WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);
            return message;
        }
        catch (JsonException ex)
        {
            LogJsonException(ex, data);
        }

        return null;
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

    internal static void CopyAdditionalHeaders(HttpRequestHeaders headers, Dictionary<string, string>? additionalHeaders, string? sessionId = null)
    {
        if (sessionId is not null)
        {
            headers.Add("mcp-session-id", sessionId);
        }

        if (additionalHeaders is null)
        {
            return;
        }

        foreach (var header in additionalHeaders)
        {
            if (!headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                throw new InvalidOperationException($"Failed to add header '{header.Key}' with value '{header.Value}' from {nameof(SseClientTransportOptions.AdditionalHeaders)}.");
            }
        }
    }
}