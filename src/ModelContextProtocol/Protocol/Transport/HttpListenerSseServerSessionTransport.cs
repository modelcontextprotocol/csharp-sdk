using Microsoft.Extensions.Logging;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.Net;
using System.Text.Json;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Implements the MCP transport protocol using <see cref="HttpListener"/>.
/// </summary>
internal sealed class HttpListenerSseServerSessionTransport : TransportBase
{
    private readonly string _serverName;
    private readonly ILogger<HttpListenerSseServerTransport> _logger;
    private SseResponseStreamTransport _responseStreamTransport;

    private string EndpointName => $"Server (SSE) ({_serverName})";

    public HttpListenerSseServerSessionTransport(string serverName, SseResponseStreamTransport responseStreamTransport, ILoggerFactory loggerFactory)
        : base(loggerFactory)
    {
        Throw.IfNull(serverName);

        _serverName = serverName;
        _responseStreamTransport = responseStreamTransport;
        _logger = loggerFactory.CreateLogger<HttpListenerSseServerTransport>();
        SetConnected(true);
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
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var json = JsonSerializer.Serialize(message, McpJsonUtilities.DefaultOptions.GetTypeInfo<IJsonRpcMessage>());
                _logger.TransportSendingMessage(EndpointName, id, json);
            }

            await _responseStreamTransport.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);

            _logger.TransportSentMessage(EndpointName, id);
        }
        catch (Exception ex)
        {
            _logger.TransportSendFailed(EndpointName, id, ex);
            throw new McpTransportException("Failed to send message", ex);
        }
    }

    public Task OnMessageReceivedAsync(IJsonRpcMessage message, CancellationToken cancellationToken)
        => WriteMessageAsync(message, cancellationToken);

    /// <inheritdoc/>
    public override ValueTask DisposeAsync()
    {
        SetConnected(false);
        return default;
    }
}
