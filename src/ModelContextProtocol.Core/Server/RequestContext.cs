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
    private IMcpServer _server;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestContext{TParams}"/> class with the specified server.
    /// </summary>
    /// <param name="server">The server with which this instance is associated.</param>
    public RequestContext(IMcpServer server)
    {
        Throw.IfNull(server);

        _server = server;
        Services = server.Services;
    }

    /// <summary>Gets or sets the server with which this instance is associated.</summary>
    public IMcpServer Server 
    {
        get => _server;
        set
        {
            Throw.IfNull(value);
            _server = value;
        }
    }

    /// <summary>Gets or sets the services associated with this request.</summary>
    /// <remarks>
    /// This may not be the same instance stored in <see cref="IMcpServer.Services"/>
    /// if <see cref="McpServerOptions.ScopeRequests"/> was true, in which case this
    /// might be a scoped <see cref="IServiceProvider"/> derived from the server's
    /// <see cref="IMcpServer.Services"/>.
    /// </remarks>
    public IServiceProvider? Services { get; set; }

    /// <summary>Gets or sets the parameters associated with this request.</summary>
    public TParams? Params { get; set; }
}