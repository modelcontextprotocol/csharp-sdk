using ModelContextProtocol.Protocol;
using System.Diagnostics;

namespace ModelContextProtocol.Server;

#pragma warning disable MCPEXP002
internal sealed class DestinationBoundMcpServer(McpServerImpl server, ITransport? transport) : McpServer
#pragma warning restore MCPEXP002
{
    public override string? SessionId => transport?.SessionId ?? server.SessionId;
    public override string? NegotiatedProtocolVersion => server.NegotiatedProtocolVersion;
    public override ClientCapabilities? ClientCapabilities => server.ClientCapabilities;
    public override Implementation? ClientInfo => server.ClientInfo;
    public override McpServerOptions ServerOptions => server.ServerOptions;
    public override IServiceProvider? Services => server.Services;
    public override LoggingLevel? LoggingLevel => server.LoggingLevel;

    public override ValueTask DisposeAsync() => server.DisposeAsync();

    public override IAsyncDisposable RegisterNotificationHandler(string method, Func<JsonRpcNotification, CancellationToken, ValueTask> handler) => server.RegisterNotificationHandler(method, handler);

    // This will throw because the server must already be running for this class to be constructed, but it should give us a good Exception message.
    public override Task RunAsync(CancellationToken cancellationToken) => server.RunAsync(cancellationToken);

    public override Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        if (message.Context is not null)
        {
            throw new ArgumentException("Only transports can provide a JsonRpcMessageContext.");
        }

        message.Context = new()
        {
            RelatedTransport = transport
        };

        return server.SendMessageAsync(message, cancellationToken);
    }

    public override Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Context is not null)
        {
            throw new ArgumentException("Only transports can provide a JsonRpcMessageContext.");
        }

        request.Context = new()
        {
            RelatedTransport = transport
        };

        return server.SendRequestAsync(request, cancellationToken);
    }
}
