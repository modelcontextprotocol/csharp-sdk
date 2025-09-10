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
/// and may be injected as parameters into <see cref="McpServerTool"/>s.
/// </remarks>
public sealed class RequestContext<TParams>
{
    /// <summary>The server with which this instance is associated.</summary>
    private McpServer _server;

    private IDictionary<string, object?>? _items;

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
        get
        {
            return _items ??= new Dictionary<string, object?>();
        }
        set
        {
            _items = value;
        }
    }

    /// <summary>Gets or sets the services associated with this request.</summary>
    /// <remarks>
    /// This may not be the same instance stored in <see cref="McpServer.Services"/>
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
}