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
public sealed class RequestContext<TParams> : MessageContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequestContext{TParams}"/> class with the specified server and JSON-RPC request.
    /// </summary>
    /// <param name="server">The server with which this instance is associated.</param>
    /// <param name="jsonRpcRequest">The JSON-RPC request associated with this context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="server"/> or <paramref name="jsonRpcRequest"/> is <see langword="null"/>.</exception>
    public RequestContext(McpServer server, JsonRpcRequest jsonRpcRequest)
        : base(server, jsonRpcRequest)
    {
    }

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
    public JsonRpcRequest JsonRpcRequest
    {
        get => (JsonRpcRequest)JsonRpcMessage;
        set => JsonRpcMessage = value;
    }

    /// <summary>
    /// Ends the current response and enables polling for updates from the server.
    /// </summary>
    /// <param name="retryInterval">The interval at which the client should poll for updates.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when polling has been enabled.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the transport does not support polling.</exception>
    public async ValueTask EnablePollingAsync(TimeSpan retryInterval, CancellationToken cancellationToken = default)
    {
        if (JsonRpcRequest.Context?.RelatedTransport is not StreamableHttpPostTransport transport)
        {
            throw new InvalidOperationException("Polling is only supported for Streamable HTTP transports.");
        }

        await transport.EnablePollingAsync(retryInterval, cancellationToken).ConfigureAwait(false);
    }
}
