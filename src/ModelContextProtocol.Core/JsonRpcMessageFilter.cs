using ModelContextProtocol.Protocol;

namespace ModelContextProtocol;

/// <summary>
/// Represents a filter that wraps the processing of incoming JSON-RPC messages.
/// </summary>
/// <param name="next">The next handler in the pipeline.</param>
/// <returns>A wrapped handler that processes messages and optionally delegates to the next handler.</returns>
internal delegate Func<JsonRpcMessage, CancellationToken, Task> JsonRpcMessageFilter(Func<JsonRpcMessage, CancellationToken, Task> next);
