using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol;

/// <summary>
/// Represents a client or server Model Context Protocol (MCP) session.
/// </summary>
/// <remarks>
/// <para>
/// The MCP session provides the core communication functionality used by both clients and servers:
/// <list type="bullet">
///   <item>Sending JSON-RPC requests and receiving responses.</item>
///   <item>Sending notifications to the connected session.</item>
///   <item>Registering handlers for receiving notifications.</item>
/// </list>
/// </para>
/// <para>
/// <see cref="McpSession"/> serves as the base interface for both <see cref="McpClient"/> and 
/// <see cref="McpServer"/> interfaces, providing the common functionality needed for MCP protocol 
/// communication. Most applications will use these more specific interfaces rather than working with 
/// <see cref="McpSession"/> directly.
/// </para>
/// <para>
/// All MCP sessions should be properly disposed after use as they implement <see cref="IAsyncDisposable"/>.
/// </para>
/// </remarks>
#pragma warning disable CS0618 // Type or member is obsolete
public abstract partial class McpSession : IMcpEndpoint, IAsyncDisposable
#pragma warning restore CS0618 // Type or member is obsolete
{
    /// <summary>Gets an identifier associated with the current MCP session.</summary>
    /// <remarks>
    /// Typically populated in transports supporting multiple sessions such as Streamable HTTP or SSE.
    /// Can return <see langword="null"/> if the session hasn't initialized or if the transport doesn't
    /// support multiple sessions (as is the case with STDIO).
    /// </remarks>
    public abstract string? SessionId { get; }

    /// <summary>
    /// Gets the negotiated protocol version for the current MCP session.
    /// </summary>
    /// <remarks>
    /// Returns the protocol version negotiated during session initialization,
    /// or <see langword="null"/> if initialization hasn't yet occurred.
    /// </remarks>
    public abstract string? NegotiatedProtocolVersion { get; }

    /// <summary>
    /// Sends a JSON-RPC request to the connected session and waits for a response.
    /// </summary>
    /// <param name="request">The JSON-RPC request to send.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task containing the session's response.</returns>
    /// <exception cref="InvalidOperationException">The transport is not connected, or another error occurs during request processing.</exception>
    /// <exception cref="McpException">An error occurred during request processing.</exception>
    /// <remarks>
    /// This method provides low-level access to send raw JSON-RPC requests. For most use cases,
    /// consider using the strongly-typed methods that provide a more convenient API.
    /// </remarks>
    public abstract Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a JSON-RPC message to the connected session.
    /// </summary>
    /// <param name="message">
    /// The JSON-RPC message to send. This can be any type that implements JsonRpcMessage, such as
    /// JsonRpcRequest, JsonRpcResponse, JsonRpcNotification, or JsonRpcError.
    /// </param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    /// <exception cref="InvalidOperationException">The transport is not connected.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This method provides low-level access to send any JSON-RPC message. For specific message types,
    /// consider using the higher-level methods such as <see cref="SendRequestAsync"/> or methods
    /// on this class that provide a simpler API.
    /// </para>
    /// <para>
    /// The method will serialize the message and transmit it using the underlying transport mechanism.
    /// </para>
    /// </remarks>
    public abstract Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default);

    /// <summary>Registers a handler to be invoked when a notification for the specified method is received.</summary>
    /// <param name="method">The notification method.</param>
    /// <param name="handler">The handler to be invoked.</param>
    /// <returns>An <see cref="IDisposable"/> that will remove the registered handler when disposed.</returns>
    public abstract IAsyncDisposable RegisterNotificationHandler(string method, Func<JsonRpcNotification, CancellationToken, ValueTask> handler);

    /// <inheritdoc/>
    public abstract ValueTask DisposeAsync();
}
