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
/// Internal implementation of the Server-Sent Events (SSE) client transport session.
/// </summary>
/// <remarks>
/// <para>
/// This class handles the active transport session between an MCP client and server using the SSE protocol.
/// It establishes and maintains an HTTP connection to receive real-time messages from the server via SSE,
/// while sending messages back to the server using HTTP POST requests.
/// </para>
/// <para>
/// The transport operates in two phases:
/// <list type="number">
///   <item>
///     <description>Connection establishment: Creates an SSE connection to the server and waits for an "endpoint" event
///     that provides the URL for sending messages back to the server.</description>
///   </item>
///   <item>
///     <description>Message exchange: Once connected, it receives messages through the SSE stream
///     and sends messages via HTTP POST to the endpoint URL.</description>
///   </item>
/// </list>
/// </para>
/// <para>
/// This transport implementation is typically created and managed by <see cref="SseClientTransport"/>,
/// which handles the creation of transport sessions and connection lifecycle.
/// </para>
/// </remarks>
internal sealed class SseClientSessionTransport : TransportBase
{
    private readonly string _endpointName;
    private readonly HttpClient _httpClient;
    private readonly SseClientTransportOptions _options;
    private readonly Uri _sseEndpoint;
    private Uri? _messageEndpoint;
    private readonly CancellationTokenSource _connectionCts;
    private Task? _receiveTask;
    private readonly ILogger _logger;
    private readonly TaskCompletionSource<bool> _connectionEstablished;

    /// <summary>
    /// Initializes a new instance of the <see cref="SseClientSessionTransport"/> class.
    /// </summary>
    /// <param name="transportOptions">Configuration options for the transport, including connection timeout, 
    /// reconnection policy, and HTTP headers.</param>
    /// <param name="serverConfig">The configuration object indicating which server to connect to,
    /// including the SSE endpoint URL.</param>
    /// <param name="httpClient">The HTTP client instance used for requests. This client is used for both
    /// establishing the SSE connection and sending messages to the server.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers. Used for diagnostic output during 
    /// transport operations. If null, a NullLogger will be used.</param>
    /// <remarks>
    /// <para>
    /// This constructor initializes the transport but does not establish a connection.
    /// Call <see cref="ConnectAsync(CancellationToken)"/> to establish the connection to the server.
    /// </para>
    /// <para>
    /// The transport uses the provided <paramref name="serverConfig"/>'s Location property
    /// as the base URL for the SSE connection. The URL should point to an SSE-compatible endpoint
    /// on the server.
    /// </para>
    /// <para>
    /// The <paramref name="httpClient"/> instance is not owned by this class and will not be
    /// disposed when the transport is disposed.
    /// </para>
    /// </remarks>
    public SseClientSessionTransport(SseClientTransportOptions transportOptions, HttpClient httpClient, ILoggerFactory? loggerFactory, string endpointName)
        : base(loggerFactory)
    {
        Throw.IfNull(transportOptions);
        Throw.IfNull(httpClient);

        _options = transportOptions;
        _sseEndpoint = transportOptions.Endpoint;
        _httpClient = httpClient;
        _connectionCts = new CancellationTokenSource();
        _logger = (ILogger?)loggerFactory?.CreateLogger<SseClientTransport>() ?? NullLogger.Instance;
        _connectionEstablished = new TaskCompletionSource<bool>();
        _endpointName = endpointName;
    }

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (IsConnected)
            {
                _logger.TransportAlreadyConnected(_endpointName);
                throw new McpTransportException("Transport is already connected");
            }

            // Start message receiving loop
            _receiveTask = ReceiveMessagesAsync(_connectionCts.Token);

            _logger.TransportReadingMessages(_endpointName);

            await _connectionEstablished.Task.WaitAsync(_options.ConnectionTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (McpTransportException)
        {
            // Rethrow transport exceptions
            throw;
        }
        catch (Exception ex)
        {
            _logger.TransportConnectFailed(_endpointName, ex);
            await CloseAsync().ConfigureAwait(false);
            throw new McpTransportException("Failed to connect transport", ex);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// For SSE client session transports, this method sends the JSON-RPC message to the server
    /// by making an HTTP POST request to the message endpoint established during connection.
    /// </para>
    /// <para>
    /// This implementation:
    /// <list type="bullet">
    ///   <item>Verifies that the transport is connected and has a valid message endpoint</item>
    ///   <item>Serializes the message to JSON</item>
    ///   <item>Creates an HTTP request with the proper Content-Type</item>
    ///   <item>Sends the request to the server and verifies the response status</item>
    /// </list>
    /// </para>
    /// <para>
    /// If the server returns a non-successful status code, this method throws a
    /// <see cref="McpTransportException"/> with details about the error.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when the transport is not connected.</exception>
    /// <exception cref="McpTransportException">Thrown when the server returns a non-successful status code.</exception>
    public override async Task SendMessageAsync(
        IJsonRpcMessage message,
        CancellationToken cancellationToken = default)
    {
        if (_messageEndpoint == null)
            throw new InvalidOperationException("Transport not connected");

        using var content = new StringContent(
            JsonSerializer.Serialize(message, McpJsonUtilities.JsonContext.Default.IJsonRpcMessage),
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
            if (string.IsNullOrEmpty(responseContent) || responseContent.Equals("accepted", StringComparison.OrdinalIgnoreCase))
            {
                _logger.SSETransportPostAccepted(_endpointName, messageId);
                // The response will arrive as an SSE message
            }
            else
            {
                JsonRpcResponse initializeResponse = JsonSerializer.Deserialize(responseContent, McpJsonUtilities.JsonContext.Default.JsonRpcResponse) ??
                    throw new McpTransportException("Failed to initialize client");

                _logger.TransportReceivedMessageParsed(_endpointName, messageId);
                await WriteMessageAsync(initializeResponse, cancellationToken).ConfigureAwait(false);
                _logger.TransportMessageWritten(_endpointName, messageId);
            }
            return;
        }

        // Otherwise, check if the response was accepted (the response will come as an SSE message)
        if (string.IsNullOrEmpty(responseContent) || responseContent.Equals("accepted", StringComparison.OrdinalIgnoreCase))
        {
            _logger.SSETransportPostAccepted(_endpointName, messageId);
        }
        else
        {
            _logger.SSETransportPostNotAccepted(_endpointName, messageId, responseContent);
            throw new McpTransportException("Failed to send message");
        }
    }

    private async Task CloseAsync()
    {
        try
        {
            await _connectionCts.CancelAsync().ConfigureAwait(false);

            if (_receiveTask != null)
            {
                await _receiveTask.ConfigureAwait(false);
            }

            _connectionCts.Dispose();
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
            await CloseAsync().ConfigureAwait(false);
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
                _logger.TransportReadMessagesCancelled(_endpointName);
                // Normal shutdown
            }
            catch (IOException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.TransportReadMessagesCancelled(_endpointName);
                // Normal shutdown
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.TransportConnectionError(_endpointName, ex);

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
            _logger.TransportMessageReceivedBeforeConnected(_endpointName, data);
            return;
        }

        try
        {
            var message = JsonSerializer.Deserialize(data, McpJsonUtilities.JsonContext.Default.IJsonRpcMessage);
            if (message == null)
            {
                _logger.TransportMessageParseUnexpectedType(_endpointName, data);
                return;
            }

            string messageId = "(no id)";
            if (message is IJsonRpcMessageWithId messageWithId)
            {
                messageId = messageWithId.Id.ToString();
            }

            _logger.TransportReceivedMessageParsed(_endpointName, messageId);
            await WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);
            _logger.TransportMessageWritten(_endpointName, messageId);
        }
        catch (JsonException ex)
        {
            _logger.TransportMessageParseFailed(_endpointName, data, ex);
        }
    }

    private void HandleEndpointEvent(string data)
    {
        try
        {
            if (string.IsNullOrEmpty(data))
            {
                _logger.TransportEndpointEventInvalid(_endpointName, data);
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
                var baseUriBuilder = new UriBuilder(_sseEndpoint);


                // Instead of manually concatenating strings, use the Uri class's composition capabilities
                _messageEndpoint = new Uri(baseUriBuilder.Uri, data);
            }

            // Set connected state
            SetConnected(true);
            _connectionEstablished.TrySetResult(true);
        }
        catch (JsonException ex)
        {
            _logger.TransportEndpointEventParseFailed(_endpointName, data, ex);
            throw new McpTransportException("Failed to parse endpoint event", ex);
        }
    }
}