namespace ModelContextProtocol.Server;

/// <summary>
/// Delegate type for handling incoming MCP requests with specific parameter and result types.
/// </summary>
/// <typeparam name="TParams">The type of the parameters sent with the request.</typeparam>
/// <typeparam name="TResult">The type of the response returned by the handler.</typeparam>
/// <param name="request">The request context containing the parameters and other metadata.</param>
/// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
/// <returns>A task representing the asynchronous operation, with the result of the handler.</returns>
public delegate ValueTask<TResult> McpRequestHandler<TParams, TResult>(
    RequestContext<TParams> request,
    CancellationToken cancellationToken);