using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

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

    /// <summary>
    /// Gets or sets the MRTR context for the current request, if any.
    /// Set by <see cref="McpServerImpl.CreateDestinationBoundServer"/> when an MRTR-aware handler invocation is in progress.
    /// </summary>
    internal MrtrContext? ActiveMrtrContext { get; set; }

    public override bool IsMrtrSupported => server.IsLowLevelMrtrAvailable();

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
        // Task-based requests (SampleAsTaskAsync/ElicitAsTaskAsync) have a "task" property on their params
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

    private async Task<JsonRpcResponse> SendRequestViaMrtrAsync(
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

    /// <inheritdoc />
    public override async ValueTask CreateTaskAsync(CancellationToken cancellationToken = default)
    {
        var deferredTask = ActiveMrtrContext?.DeferredTask
            ?? throw new InvalidOperationException(
                "CreateTaskAsync can only be called from a tool handler with DeferTaskCreation enabled " +
                "when the client provides task metadata in the tools/call request.");

        // Signal the framework to create the task and wait for acknowledgment.
        // RequestTaskCreationAsync is atomic — throws if already called.
        var result = await deferredTask.RequestTaskCreationAsync(cancellationToken).ConfigureAwait(false);

        // Transition to task mode on the handler's async flow.
        TaskExecutionContext.Current = new TaskExecutionContext
        {
            TaskId = result.TaskId,
            SessionId = result.SessionId,
            TaskStore = result.TaskStore,
            SendNotifications = result.SendNotifications,
            NotifyTaskStatusFunc = result.NotifyTaskStatusFunc,
        };

        // No more ephemeral MRTR — subsequent ElicitAsync/SampleAsync calls
        // will go through SendRequestWithTaskStatusTrackingAsync instead.
        ActiveMrtrContext = null;
    }
}
