namespace ModelContextProtocol.Server;

/// <summary>
/// Delegate type for applying filters to incoming MCP requests with specific parameter and result types.
/// </summary>
/// <typeparam name="TParams">The type of the parameters sent with the request.</typeparam>
/// <typeparam name="TResult">The type of the response returned by the handler.</typeparam>
/// <param name="next">The next request handler in the pipeline.</param>
/// <returns>The next request handler wrapped with the filter.</returns>
public delegate McpRequestHandler<TParams, TResult> McpRequestFilter<TParams, TResult>(
    McpRequestHandler<TParams, TResult> next);