using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Utils.Json;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Server;
using ModelContextProtocol.Utils;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Implements the MCP transport protocol using <see cref="HttpListener"/>.
/// </summary>
public sealed class HttpListenerSseServerTransport : TransportBase, IServerTransport
{
    private readonly string _serverName;
    private readonly HttpListenerServerProvider _httpServerProvider;
    private readonly ILogger<HttpListenerSseServerTransport> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private CancellationTokenSource? _shutdownCts;
    
    private string EndpointName => $"Server (SSE) ({_serverName})";

    /// <summary>
    /// Initializes a new instance of the SseServerTransport class.
    /// </summary>
    /// <param name="serverOptions">The server options.</param>
    /// <param name="port">The port to listen on.</param>
    /// <param name="loggerFactory">A logger factory for creating loggers.</param>
    public HttpListenerSseServerTransport(McpServerOptions serverOptions, int port, ILoggerFactory loggerFactory)
        : this(GetServerName(serverOptions), port, loggerFactory)
    {
    }

    /// <summary>
    /// Initializes a new instance of the SseServerTransport class.
    /// </summary>
    /// <param name="serverName">The name of the server.</param>
    /// <param name="port">The port to listen on.</param>
    /// <param name="loggerFactory">A logger factory for creating loggers.</param>
    public HttpListenerSseServerTransport(string serverName, int port, ILoggerFactory loggerFactory)
        : base(loggerFactory)
    {
        _serverName = serverName;
        _logger = loggerFactory.CreateLogger<HttpListenerSseServerTransport>();
        _jsonOptions = McpJsonUtilities.DefaultOptions;
        _httpServerProvider = new HttpListenerServerProvider(port);
    }

    /// <inheritdoc/>
    public Task StartListeningAsync(CancellationToken cancellationToken = default)
    {
        _shutdownCts = new CancellationTokenSource();

        _httpServerProvider.InitializeMessageHandler(HttpMessageHandler);
        _httpServerProvider.StartAsync(cancellationToken);

        SetConnected(true);

        return Task.CompletedTask;
    }


    /// <inheritdoc/>
    public override async Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            _logger.TransportNotConnected(EndpointName);
            throw new McpTransportException("Transport is not connected");
        }

        string id = "(no id)";
        if (message is IJsonRpcMessageWithId messageWithId)
        {
            id = messageWithId.Id.ToString();
        }

        try
        {
            var json = JsonSerializer.Serialize(message, _jsonOptions.GetTypeInfo<IJsonRpcMessage>());
            _logger.TransportSendingMessage(EndpointName, id, json);

            await _httpServerProvider.SendEvent(json, "message").ConfigureAwait(false);

            _logger.TransportSentMessage(EndpointName, id);
        }
        catch (Exception ex)
        {
            _logger.TransportSendFailed(EndpointName, id, ex);
            throw new McpTransportException("Failed to send message", ex);
        }
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await CleanupAsync(CancellationToken.None).ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        _logger.TransportCleaningUp(EndpointName);

        if (_shutdownCts != null)
        {
            await _shutdownCts.CancelAsync().ConfigureAwait(false);
            _shutdownCts.Dispose();
            _shutdownCts = null;
        }

        _httpServerProvider.Dispose();

        SetConnected(false);
        _logger.TransportCleanedUp(EndpointName);
    }

    /// <summary>
    /// Handles HTTP messages received by the HTTP server provider.
    /// </summary>
    /// <returns>true if the message was accepted (return 202), false otherwise (return 400)</returns>
    private bool HttpMessageHandler(string request, CancellationToken cancellationToken)
    {
        _logger.TransportReceivedMessage(EndpointName, request);

        try
        {
            var message = JsonSerializer.Deserialize(request, _jsonOptions.GetTypeInfo<IJsonRpcMessage>());
            if (message != null)
            {
                // Fire-and-forget the message to the message channel
                Task.Run(async () =>
                {
                    string messageId = "(no id)";
                    if (message is IJsonRpcMessageWithId messageWithId)
                    {
                        messageId = messageWithId.Id.ToString();
                    }

                    _logger.TransportReceivedMessageParsed(EndpointName, messageId);
                    await WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);
                    _logger.TransportMessageWritten(EndpointName, messageId);
                }, cancellationToken);

                return true;
            }
            else
            {
                _logger.TransportMessageParseUnexpectedType(EndpointName, request);
                return false;
            }
        }
        catch (JsonException ex)
        {
            _logger.TransportMessageParseFailed(EndpointName, request, ex);
            return false;
        }
    }

    /// <summary>Validates the <paramref name="serverOptions"/> and extracts from it the server name to use.</summary>
    private static string GetServerName(McpServerOptions serverOptions)
    {
        Throw.IfNull(serverOptions);
        Throw.IfNull(serverOptions.ServerInfo);

        return serverOptions.ServerInfo.Name;
    }
}
