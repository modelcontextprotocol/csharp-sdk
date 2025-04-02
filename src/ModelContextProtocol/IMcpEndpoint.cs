using ModelContextProtocol.Protocol.Messages;

namespace ModelContextProtocol;

/// <summary>Represents a client or server MCP endpoint.</summary>
public interface IMcpEndpoint : IAsyncDisposable
{
    /// <summary>Sends a generic JSON-RPC request to the connected endpoint.</summary>
    /// <typeparam name="TResult">The expected response type.</typeparam>
    /// <param name="request">The JSON-RPC request to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task containing the client's response.</returns>
    Task<TResult> SendRequestAsync<TResult>(JsonRpcRequest request, CancellationToken cancellationToken = default) where TResult : class;

    /// <summary>Sends a message to the connected endpoint.</summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a handler for server notifications of a specific method.
    /// </summary>
    /// <param name="method">The notification method to handle.</param>
    /// <param name="handler">The async handler function to process notifications.</param>
    /// <remarks>
    /// <para>
    /// Each method may have multiple handlers. Adding a handler for a method that already has one
    /// will not replace the existing handler.
    /// </para>
    /// <para>
    /// <see cref="NotificationMethods"> provides constants for common notification methods.</see>
    /// </para>
    /// </remarks>
    void AddNotificationHandler(string method, Func<JsonRpcNotification, Task> handler);
}
