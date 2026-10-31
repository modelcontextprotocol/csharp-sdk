using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Client;

/// <summary>
/// Delegate type for applying filters to outgoing MCP requests with specific parameter and result types from a client.
/// </summary>
/// <typeparam name="TParams">The type of the parameters sent with the request.</typeparam>
/// <typeparam name="TResult">The type of the result returned for the request.</typeparam>
/// <param name="next">The next request handler in the pipeline.</param>
/// <returns>The next request handler wrapped with the filter.</returns>
[Experimental(Experimentals.Extensibility_DiagnosticId, UrlFormat = Experimentals.Extensibility_Url)]
public delegate McpClientRequestHandler<TParams, TResult> McpClientRequestFilter<TParams, TResult>(
    McpClientRequestHandler<TParams, TResult> next);
