using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Client;

/// <summary>
/// Delegate type for sending outgoing MCP requests with specific parameter and result types from a client.
/// </summary>
/// <typeparam name="TParams">The type of the parameters sent with the request.</typeparam>
/// <typeparam name="TResult">The type of the result returned for the request.</typeparam>
/// <param name="request">The request context containing the parameters and other metadata.</param>
/// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
/// <returns>A task representing the asynchronous operation, with the result of the request.</returns>
[Experimental(Experimentals.Extensibility_DiagnosticId, UrlFormat = Experimentals.Extensibility_Url)]
public delegate ValueTask<TResult> McpClientRequestHandler<TParams, TResult>(
    McpClientRequestContext<TParams> request,
    CancellationToken cancellationToken);
