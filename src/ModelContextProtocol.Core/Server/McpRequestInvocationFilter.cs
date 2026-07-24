using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Server;

/// <summary>
/// Delegate type for filtering a single incoming MCP request invocation.
/// </summary>
/// <typeparam name="TParams">The type of the parameters sent with the request.</typeparam>
/// <typeparam name="TResult">The type of the response returned by the handler.</typeparam>
/// <param name="context">The context for the current request.</param>
/// <param name="next">The next request handler in the pipeline for this invocation.</param>
/// <param name="cancellationToken">The cancellation token for the current request.</param>
/// <returns>The result of the filtered request invocation.</returns>
[Experimental(Experimentals.Subclassing_DiagnosticId, UrlFormat = Experimentals.Subclassing_Url)]
public delegate ValueTask<TResult> McpRequestInvocationFilter<TParams, TResult>(
    RequestContext<TParams> context,
    McpRequestHandler<TParams, TResult> next,
    CancellationToken cancellationToken);
