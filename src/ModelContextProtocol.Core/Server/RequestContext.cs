using System.Security.Claims;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides a context container that provides access to the client request parameters and resources for the request.
/// </summary>
/// <typeparam name="TParams">Type of the request parameters specific to each MCP operation.</typeparam>
/// <remarks>
/// The <see cref="RequestContext{TParams}"/> encapsulates all contextual information for handling an MCP request.
/// This type is typically received as a parameter in handler delegates registered with IMcpServerBuilder,
/// and can be injected as parameters into <see cref="McpServerTool"/>s.
/// </remarks>
public sealed class RequestContext<TParams>
{
    /// <summary>The server with which this instance is associated.</summary>
    private McpServer _server;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestContext{TParams}"/> class with the specified server and JSON-RPC request.
    /// </summary>
    /// <param name="server">The server with which this instance is associated.</param>
    /// <param name="jsonRpcRequest">The JSON-RPC request associated with this context.</param>
    public RequestContext(McpServer server, JsonRpcRequest jsonRpcRequest)
    {
        Throw.IfNull(server);
        Throw.IfNull(jsonRpcRequest);

        _server = server;
        JsonRpcRequest = jsonRpcRequest;
        Services = server.Services;
        User = jsonRpcRequest.Context?.User;
    }

    /// <summary>Gets or sets the server with which this instance is associated.</summary>
    public McpServer Server
    {
        get => _server;
        set
        {
            Throw.IfNull(value);
            _server = value;
        }
    }

    /// <summary>
    /// Gets or sets a key/value collection that can be used to share data within the scope of this request.
    /// </summary>
    public IDictionary<string, object?> Items
    {
        get => field ??= new Dictionary<string, object?>();
        set => field = value;
    }

    /// <summary>Gets or sets the services associated with this request.</summary>
    /// <remarks>
    /// This provider might not be the same instance stored in <see cref="McpServer.Services"/>
    /// if <see cref="McpServerOptions.ScopeRequests"/> was true, in which case this
    /// might be a scoped <see cref="IServiceProvider"/> derived from the server's
    /// <see cref="McpServer.Services"/>.
    /// </remarks>
    public IServiceProvider? Services { get; set; }

    /// <summary>Gets or sets the user associated with this request.</summary>
    public ClaimsPrincipal? User { get; set; }

    /// <summary>Gets or sets the parameters associated with this request.</summary>
    public TParams? Params { get; set; }

    /// <summary>
    /// Gets or sets the primitive that matched the request.
    /// </summary>
    public IMcpServerPrimitive? MatchedPrimitive { get; set; }

    /// <summary>
    /// Gets the JSON-RPC request associated with this context.
    /// </summary>
    /// <remarks>
    /// This property provides access to the complete JSON-RPC request that initiated this handler invocation,
    /// including the method name, parameters, request ID, and associated transport and user information.
    /// </remarks>
    public JsonRpcRequest JsonRpcRequest { get; }

    /// <summary>
    /// Closes the SSE stream for the current request, signaling to the client that it should reconnect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method implements the SSE polling pattern from SEP-1699. When called, it gracefully closes
    /// the SSE stream for the current request. For this to work correctly, the server must have sent
    /// a priming event with an event ID before this method is called (which happens automatically when
    /// resumability is enabled via <see cref="StreamableHttpServerTransport.EventStore"/>).
    /// </para>
    /// <para>
    /// After calling this method, the client will receive a stream end and should reconnect with the
    /// Last-Event-ID header. The server will then replay any events that were sent after that ID
    /// and continue streaming new events.
    /// </para>
    /// <para>
    /// This method only has an effect when using the Streamable HTTP transport with resumability enabled.
    /// For other transports or when resumability is not configured, this method does nothing.
    /// </para>
    /// </remarks>
    public void CloseSseStream()
    {
        JsonRpcRequest.Context?.CloseSseStream?.Invoke();
    }

    /// <summary>
    /// Closes the standalone SSE stream for server-initiated messages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method closes the standalone SSE stream that is used for server-initiated messages
    /// (notifications and requests from server to client). Unlike <see cref="CloseSseStream"/>,
    /// this affects the session-level SSE stream (from GET requests) rather than a request-specific stream.
    /// </para>
    /// <para>
    /// This is useful when the server needs to signal the client to reconnect its standalone SSE stream,
    /// for example during server restarts or resource cleanup.
    /// </para>
    /// <para>
    /// This method only has an effect when using the Streamable HTTP transport.
    /// For other transports, this method does nothing.
    /// </para>
    /// </remarks>
    public void CloseStandaloneSseStream()
    {
        JsonRpcRequest.Context?.CloseStandaloneSseStream?.Invoke();
    }
}
