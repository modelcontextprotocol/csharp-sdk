using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Utils.Json;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Server;
using ModelContextProtocol.Utils;
using System.Threading.Channels;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Implements the MCP transport protocol using <see cref="HttpListener"/>.
/// </summary>
public sealed class HttpListenerSseServerTransport : IServerTransport, IAsyncDisposable
{
    private readonly string _serverName;
    private readonly HttpListenerServerProvider _httpServerProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<HttpListenerSseServerTransport> _logger;

    private readonly Channel<ITransport> _incomingSessions;

    private HttpListenerSseServerSessionTransport? _sessionTransport;

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
    {
        Throw.IfNull(serverName);

        _serverName = serverName;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<HttpListenerSseServerTransport>();
        _httpServerProvider = new HttpListenerServerProvider(port)
        {
            OnSseConnectionAsync = OnSseConnectionAsync,
            OnMessageAsync = OnMessageAsync,
        };

        // Until we support session IDs, there's no way to support more than one concurrent session.
        // Any new SSE connection overwrites the old session and any new /messages go to the new session.
        _incomingSessions = Channel.CreateBounded<ITransport>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });

        // REVIEW: We could add another layer of async for binding similar to Kestrel's IConnectionListenerFactory,
        // but this wouldn't play well with a static factory method to accept new sessions. Ultimately,
        // ASP.NET Core is not going to hand over binding to the MCP SDK, so I decided to just bind in the transport
        // constructor for now.
        _httpServerProvider.Start();
    }

    /// <inheritdoc/>
    public async Task<ITransport?> AcceptAsync(CancellationToken cancellationToken = default)
    {
        while (await _incomingSessions.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_incomingSessions.Reader.TryRead(out var session))
            {
                return session;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _logger.TransportCleaningUp(EndpointName);

        await _httpServerProvider.DisposeAsync().ConfigureAwait(false);
        _incomingSessions.Writer.TryComplete();

        _logger.TransportCleanedUp(EndpointName);
    }

    private async Task OnSseConnectionAsync(Stream responseStream, CancellationToken cancellationToken)
    {
        var sseResponseStreamTransport = new SseResponseStreamTransport(responseStream);
        var sessionTransport = new HttpListenerSseServerSessionTransport(_serverName, sseResponseStreamTransport, _loggerFactory);

        await using (sseResponseStreamTransport.ConfigureAwait(false))
        await using (sseResponseStreamTransport.ConfigureAwait(false))
        {
            _sessionTransport = sessionTransport;
            await _incomingSessions.Writer.WriteAsync(sessionTransport).ConfigureAwait(false);
            await sseResponseStreamTransport.RunAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Handles HTTP messages received by the HTTP server provider.
    /// </summary>
    /// <returns>true if the message was accepted (return 202), false otherwise (return 400)</returns>
    private async Task<bool> OnMessageAsync(Stream requestStream, CancellationToken cancellationToken)
    {
        string request;
        IJsonRpcMessage? message = null;

        if (_logger.IsEnabled(LogLevel.Information))
        {
            using var reader = new StreamReader(requestStream);
            request = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            message = JsonSerializer.Deserialize(request, McpJsonUtilities.DefaultOptions.GetTypeInfo<IJsonRpcMessage>());

            _logger.TransportReceivedMessage(EndpointName, request);
        }
        else
        {
            request = "(Enable information-level logs to see the request)";
        }

        try
        {
            message ??= await JsonSerializer.DeserializeAsync(requestStream, McpJsonUtilities.DefaultOptions.GetTypeInfo<IJsonRpcMessage>()).ConfigureAwait(false);
            if (message != null)
            {
                string messageId = "(no id)";
                if (message is IJsonRpcMessageWithId messageWithId)
                {
                    messageId = messageWithId.Id.ToString();
                }

                _logger.TransportReceivedMessageParsed(EndpointName, messageId);

                if (_sessionTransport is null)
                {
                    return false;
                }

                await _sessionTransport.OnMessageReceivedAsync(message, cancellationToken).ConfigureAwait(false);

                _logger.TransportMessageWritten(EndpointName, messageId);

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
        Throw.IfNull(serverOptions.ServerInfo.Name);

        return serverOptions.ServerInfo.Name;
    }
}
