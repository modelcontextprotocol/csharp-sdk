using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Server;

#pragma warning disable MCPEXP002
internal sealed class DestinationBoundMcpServer(McpServerImpl server, ITransport? transport, JsonRpcMessageContext? requestContext = null) : McpServer
#pragma warning restore MCPEXP002
{
    private readonly bool _isJuly2026OrLaterRequest = server.IsJuly2026OrLaterProtocolRequest(requestContext);
    private readonly ClientCapabilities? _requestClientCapabilities = requestContext?.ClientCapabilities;
    private readonly Implementation? _requestClientInfo = requestContext?.ClientInfo;

    public override string? SessionId => transport?.SessionId ?? server.SessionId;
    public override string? NegotiatedProtocolVersion => server.NegotiatedProtocolVersion;
    public override McpServerOptions ServerOptions => server.ServerOptions;
    public override IServiceProvider? Services => server.Services;
    [Obsolete(Obsoletions.DeprecatedLogging_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
    public override LoggingLevel? LoggingLevel => server.LoggingLevel;

    public override ClientCapabilities? ClientCapabilities
    {
        get
        {
            // In stateless transport mode, a single request does not have a persistent bidirectional channel.
            // Server-to-client requests (sampling, roots, elicitation) are unsupported in this mode and the
            // capability gates rely on a null ClientCapabilities value to report that unsupported-state path.
            if (!server.HasStatefulTransport())
            {
                return null;
            }

            // On protocol revision 2026-07-28+, client capabilities are request-scoped (_meta on each request)
            // and must not be inferred from prior requests. Missing per-request capabilities therefore means
            // "no declared capabilities for this request", represented by an empty object. A fresh instance is
            // returned deliberately: ClientCapabilities is a mutable DTO handed to user handlers, so a shared
            // static empty instance could be mutated and leak across requests.
            if (_isJuly2026OrLaterRequest)
            {
                return _requestClientCapabilities ?? new ClientCapabilities();
            }

            // Legacy protocol behavior uses session-scoped capabilities established during initialize (or
            // pre-populated migration data), so ignore per-request values and return the server session state.
            return server.ClientCapabilities;
        }
    }

    public override Implementation? ClientInfo
    {
        get
        {
            // On protocol revision 2026-07-28+, client info is request-scoped (carried in each request's _meta),
            // mirroring how ClientCapabilities is resolved above. Return only this request's declared value and
            // do not fall back to shared session state, which under a stateful transport could belong to a
            // different concurrent request.
            if (_isJuly2026OrLaterRequest)
            {
                return _requestClientInfo;
            }

            // Legacy protocol behavior uses session-scoped client info established during initialize.
            return server.ClientInfo;
        }
    }

    /// <summary>
    /// Gets or sets the MRTR context for the current request, if any.
    /// Set by <see cref="McpServerImpl.CreateDestinationBoundServer"/> when an MRTR-aware handler invocation is in progress.
    /// </summary>
    internal MrtrContext? ActiveMrtrContext { get; set; }

    public override bool IsMrtrSupported => server.IsMrtrSupported;

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
        // When an MRTR context is active, intercept server-to-client requests (sampling, elicitation, roots)
        // and route them through the MRTR mechanism instead of sending them over the wire.
        // Task-augmented requests (SampleAsTaskAsync/ElicitAsTaskAsync) have a "task" property on their params
        // and expect a CreateTaskResult response, so they must bypass MRTR and go over the wire.
        if (ActiveMrtrContext is { } mrtrContext &&
            !(request.Params is JsonObject paramsObj && paramsObj.ContainsKey("task")))
        {
            return SendRequestViaMrtrAsync(mrtrContext, request, cancellationToken);
        }

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

    private static async Task<JsonRpcResponse> SendRequestViaMrtrAsync(
        MrtrContext mrtrContext, JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var inputRequest = new InputRequest
        {
            Method = request.Method,
            Params = request.Params is { } paramsNode
                ? JsonSerializer.Deserialize(paramsNode, McpJsonUtilities.JsonContext.Default.JsonElement)
                : null,
        };
        var inputResponse = await mrtrContext.RequestInputAsync(inputRequest, cancellationToken).ConfigureAwait(false);

        return new JsonRpcResponse
        {
            Id = request.Id,
            Result = JsonSerializer.SerializeToNode(inputResponse.RawValue, McpJsonUtilities.JsonContext.Default.JsonElement),
        };
    }
}
