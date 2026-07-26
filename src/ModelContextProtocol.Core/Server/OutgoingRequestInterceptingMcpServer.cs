using ModelContextProtocol.Protocol;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Server;

#pragma warning disable MCPEXP002
internal sealed class OutgoingRequestInterceptingMcpServer(
    McpServer server,
    Func<string, JsonNode?, CancellationToken, ValueTask<JsonNode?>> interceptor) : McpServer
#pragma warning restore MCPEXP002
{
    internal override Func<string, JsonNode?, CancellationToken, ValueTask<JsonNode?>>? OutgoingRequestInterceptor => interceptor;

    public override string? SessionId => server.SessionId;

    public override string? NegotiatedProtocolVersion => server.NegotiatedProtocolVersion;

    public override ClientCapabilities? ClientCapabilities => server.ClientCapabilities;

    public override Implementation? ClientInfo => server.ClientInfo;

    public override McpServerOptions ServerOptions => server.ServerOptions;

    public override IServiceProvider? Services => server.Services;

    [Obsolete(Obsoletions.DeprecatedLogging_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
    public override LoggingLevel? LoggingLevel => server.LoggingLevel;

    public override bool IsMrtrSupported => server.IsMrtrSupported;

    public override ValueTask DisposeAsync() => server.DisposeAsync();

    public override IAsyncDisposable RegisterNotificationHandler(
        string method,
        Func<JsonRpcNotification, CancellationToken, ValueTask> handler) =>
        server.RegisterNotificationHandler(method, handler);

    public override Task RunAsync(CancellationToken cancellationToken = default) =>
        server.RunAsync(cancellationToken);

    public override Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default) =>
        server.SendMessageAsync(message, cancellationToken);

    public override async Task<JsonRpcResponse> SendRequestAsync(
        JsonRpcRequest request,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(request);

        return new JsonRpcResponse
        {
            Id = request.Id,
            Result = await interceptor(request.Method, request.Params, cancellationToken).ConfigureAwait(false),
        };
    }
}
