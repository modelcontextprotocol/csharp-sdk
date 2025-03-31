using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// The ServerSideEvents client transport implementation
/// </summary>
internal sealed class SseClientSessionTransport : TransportBase
{
    private readonly HttpClient _httpClient;
    private readonly SseClientTransportOptions _options;
    private readonly Uri _sseEndpoint;
    private Uri? _messageEndpoint;
    private readonly CancellationTokenSource _connectionCts;
    private Task? _receiveTask;
    private readonly ILogger _logger;
    private readonly McpServerConfig _serverConfig;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly TaskCompletionSource<bool> _connectionEstablished;

    private string EndpointName => $"Client (SSE) for ({_serverConfig.Id}: {_serverConfig.Name})";

    /// <summary>
    /// SSE transport for client endpoints. Unlike stdio it does not launch a process, but connects to an existing server.
    /// The HTTP server can be local or remote, and must support the SSE protocol.
    /// </summary>
    /// <param name="transportOptions">Configuration options for the transport.</param>
    /// <param name="serverConfig">The configuration object indicating which server to connect to.</param>
    /// <param name="httpClient">The HTTP client instance used for requests.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    public SseClientSessionTransport(SseClientTransportOptions transportOptions, McpServerConfig serverConfig, HttpClient httpClient, ILoggerFactory? loggerFactory)
        : base(loggerFactory)
    {
        Throw.IfNull(transportOptions);
        Throw.IfNull(serverConfig);
        Throw.IfNull(httpClient);

        _options = transportOptions;
        _serverConfig = serverConfig;
        _sseEndpoint = new Uri(serverConfig.Location!);
        _httpClient = httpClient;
        _connectionCts = new CancellationTokenSource();
        _logger = (ILogger?)loggerFactory?.CreateLogger<SseClientTransport>() ?? NullLogger.Instance;
        _jsonOptions = McpJsonUtilities.DefaultOptions;
        _connectionEstablished = new TaskCompletionSource<bool>();
    }

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (IsConnected)
            {
                _logger.TransportAlreadyConnected(EndpointName);
                throw new McpTransportException("Transport is already connected");
            }

            // Start message receiving loop
            _receiveTask = ReceiveMessagesAsync(_connectionCts.Token);

            _logger.TransportReadingMessages(EndpointName);

            await _connectionEstablished.Task.WaitAsync(_options.ConnectionTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (McpTransportException)
        {
            // Rethrow transport exceptions
            throw;
        }
        catch (Exception ex)
        {
            _logger.TransportConnectFailed(EndpointName, ex);
            await CloseAsync().ConfigureAwait(false);
            throw new McpTransportException("Failed to connect transport", ex);
        }
    }

    /// <inheritdoc/>
    public override async Task SendMessageAsync(
        IJsonRpcMessage message,
        CancellationToken cancellationToken = default)
    {
        if (_messageEndpoint == null)
            throw new InvalidOperationException("Transport not connected");

        using var content = new StringContent(
            JsonSerializer.Serialize(message, _jsonOptions.GetTypeInfo<IJsonRpcMessage>()),
            Encoding.UTF8,
            "application/json"
        );

        string messageId = "(no id)";

        if (message is IJsonRpcMessageWithId messageWithId)
        {
            messageId = messageWithId.Id.ToString();
        }

        var response = await _httpClient.PostAsync(
            _messageEndpoint,
            content,
            cancellationToken
        ).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // Check if the message was an initialize request
        if (message is JsonRpcRequest request && request.Method == RequestMethods.Initialize)
        {
            // If the response is not a JSON-RPC response, it is an SSE message
            if (responseContent.Equals("accepted", StringComparison.OrdinalIgnoreCase))
            {
                _logger.SSETransportPostAccepted(EndpointName, messageId);
                // The response will arrive as an SSE message
            }
            else
            {
                JsonRpcResponse initializeResponse = JsonSerializer.Deserialize(responseContent, _jsonOptions.GetTypeInfo<JsonRpcResponse>()) ??
                    throw new McpTransportException("Failed to initialize client");

                _logger.TransportReceivedMessageParsed(EndpointName, messageId);
                await WriteMessageAsync(initializeResponse, cancellationToken).ConfigureAwait(false);
                _logger.TransportMessageWritten(EndpointName, messageId);
            }
            return;
        }

        // Otherwise, check if the response was accepted (the response will come as an SSE message)
        if (responseContent.Equals("accepted", StringComparison.OrdinalIgnoreCase))
        {
            _logger.SSETransportPostAccepted(EndpointName, messageId);
        }
        else
        {
            _logger.SSETransportPostNotAccepted(EndpointName, messageId, responseContent);
            throw new McpTransportException("Failed to send message");
        }
    }

    private async Task CloseAsync()
    {
        try
        {
            if (!_connectionCts.IsCancellationRequested)
            {
                await _connectionCts.CancelAsync().ConfigureAwait(false);
                _connectionCts.Dispose();
            }

            if (_receiveTask != null)
            {
                await _receiveTask.ConfigureAwait(false);
            }
        }
        finally
        {
            SetConnected(false);
        }
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        try
        {
            await CloseAsync();
        }
        catch (Exception)
        {
            // Ignore exceptions on close
        }
    }

    internal Uri? MessageEndpoint => _messageEndpoint;

    internal SseClientTransportOptions Options => _options;

    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
        int reconnectAttempts = 0;

        while (!cancellationToken.IsCancellationRequested && !IsConnected)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _sseEndpoint);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken
                ).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

                await foreach (SseItem<string> sseEvent in SseParser.Create(stream).EnumerateAsync(cancellationToken).ConfigureAwait(false))
                {
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
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.TransportReadMessagesCancelled(EndpointName);
                // Normal shutdown
            }
            catch (IOException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.TransportReadMessagesCancelled(EndpointName);
                // Normal shutdown
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.TransportConnectionError(EndpointName, ex);

                reconnectAttempts++;
                if (reconnectAttempts >= _options.MaxReconnectAttempts)
                {
                    throw new McpTransportException("Exceeded reconnect limit", ex);
                }

                await Task.Delay(_options.ReconnectDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        SetConnected(false);
    }

    private async Task ProcessSseMessage(string data, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            _logger.TransportMessageReceivedBeforeConnected(EndpointName, data);
            return;
        }

        try
        {
            var message = JsonSerializer.Deserialize(data, _jsonOptions.GetTypeInfo<IJsonRpcMessage>());
            if (message == null)
            {
                _logger.TransportMessageParseUnexpectedType(EndpointName, data);
                return;
            }

            string messageId = "(no id)";
            if (message is IJsonRpcMessageWithId messageWithId)
            {
                messageId = messageWithId.Id.ToString();
            }

            _logger.TransportReceivedMessageParsed(EndpointName, messageId);
            await WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);
            _logger.TransportMessageWritten(EndpointName, messageId);
        }
        catch (JsonException ex)
        {
            _logger.TransportMessageParseFailed(EndpointName, data, ex);
        }
    }

    private void HandleEndpointEvent(string data)
    {
        try
        {
            if (string.IsNullOrEmpty(data))
            {
                _logger.TransportEndpointEventInvalid(EndpointName, data);
                return;
            }

            // Check if data is absolute URI
            if (data.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || data.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Since the endpoint is an absolute URI, we can use it directly
                _messageEndpoint = new Uri(data);
            }
            else
            {
                // If the endpoint is a relative URI, we need to combine it with the relative path of the SSE endpoint
                var hostUrl = _sseEndpoint.AbsoluteUri;
                if (hostUrl.EndsWith("/sse", StringComparison.Ordinal))
                    hostUrl = hostUrl[..^4];

                var endpointUri = $"{hostUrl.TrimEnd('/')}/{data.TrimStart('/')}";

                _messageEndpoint = new Uri(endpointUri);
            }

            // Set connected state
            SetConnected(true);
            _connectionEstablished.TrySetResult(true);
        }
        catch (JsonException ex)
        {
            _logger.TransportEndpointEventParseFailed(EndpointName, data, ex);
            throw new McpTransportException("Failed to parse endpoint event", ex);
        }
    }
}