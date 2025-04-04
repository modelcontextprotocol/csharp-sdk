using ModelContextProtocol.Protocol.Messages;

namespace ModelContextProtocol;

/// <summary>Represents a client or server MCP endpoint.</summary>
public interface IMcpEndpoint : IAsyncDisposable
{
    /// <summary>Sends a JSON-RPC request to the connected endpoint.</summary>
    /// <param name="request">The JSON-RPC request to send.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task containing the client's response.</returns>
    Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sends a message to the connected endpoint.</summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default);
}
