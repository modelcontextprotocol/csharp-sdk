using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Types;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents a server that can communicate with a client using the MCP protocol.
/// </summary>
public interface IMcpServer : IAsyncDisposable
{
    /// <summary>
    /// Gets the capabilities supported by the client.
    /// </summary>
    ClientCapabilities? ClientCapabilities { get; }

    /// <summary>
    /// Gets the version and implementation information of the client.
    /// </summary>
    Implementation? ClientInfo { get; }

    /// <summary>Gets the options used to construct this server.</summary>
    McpServerOptions ServerOptions { get; }

    /// <summary>
    /// Gets the service provider for the server.
    /// </summary>
    IServiceProvider? Services { get; }

    /// <summary>
    /// Adds a handler for client notifications of a specific method.
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

    /// <summary>
    /// Runs the server, listening for and handling client requests.
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a generic JSON-RPC request to the client.
    /// NB! This is a temporary method that is available to send not yet implemented feature messages.
    /// Once all MCP features are implemented this will be made private, as it is purely a convenience for those who wish to implement features ahead of the library.
    /// </summary>
    /// <typeparam name="TResult">The expected response type.</typeparam>
    /// <param name="request">The JSON-RPC request to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task containing the client's response.</returns>
    Task<TResult> SendRequestAsync<TResult>(JsonRpcRequest request, CancellationToken cancellationToken = default) where TResult : class;

    /// <summary>
    /// Sends a message to the client.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default);
}
