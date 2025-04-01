using ModelContextProtocol.Protocol.Messages;

namespace ModelContextProtocol.Shared;

/// <summary>
/// Class for managing an MCP JSON-RPC session. This covers both MCP clients and servers.
/// </summary>
public interface IMcpSession : IDisposable
{
    /// <summary>
    /// The name of the endpoint for logging and debug purposes.
    /// </summary>
    string EndpointName { get; set; }

    /// <summary>
    /// Starts processing messages from the transport. This method will block until the transport is disconnected.
    /// This is generally started in a background task or thread from the initialization logic of the derived class.
    /// </summary>
    Task ProcessMessagesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sends a generic JSON-RPC request to the server.
    /// </summary>
    /// <param name="message">The JSON-RPC request to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task containing the server's response.</returns>
    Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a request over the protocol
    /// </summary>
    /// <typeparam name="TResult">The MCP Response type.</typeparam>
    /// <param name="request">The request instance</param>
    /// <param name="cancellationToken">The token for cancellation.</param>
    /// <returns>The MCP response.</returns>
    Task<TResult> SendRequestAsync<TResult>(JsonRpcRequest request, CancellationToken cancellationToken = default) where TResult : class;
}