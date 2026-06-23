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
/// <see cref="McpSession"/> serves as the base class for both <see cref="McpClient"/> and
/// <see cref="McpServer"/>, providing the common functionality needed for MCP protocol
/// communication. Most applications will use these more specific interfaces rather than working with
/// <see cref="McpSession"/> directly.
/// </para>
/// <para>
/// All MCP sessions should be properly disposed after use as they implement <see cref="IAsyncDisposable"/>.
/// </para>
/// </remarks>
public abstract partial class McpSession : IAsyncDisposable
{
    /// <summary>The latest stable protocol revision this SDK supports.</summary>
    /// <remarks>
    /// Set <see cref="McpClientOptions.ProtocolVersion"/> or <see cref="McpServerOptions.ProtocolVersion"/>
    /// to this value to explicitly pin to the current stable revision instead of accepting whatever
    /// the runtime negotiates.
    /// </remarks>
    public const string LatestProtocolVersion = McpSessionHandler.LatestProtocolVersion;

    /// <summary>The in-progress draft protocol revision this SDK supports.</summary>
    /// <remarks>
    /// The draft revision removes the <c>initialize</c> handshake (SEP-2575) and the
    /// <c>Mcp-Session-Id</c> header (SEP-2567), so it is sessionless on the wire and over HTTP is only
    /// served when the server is stateless. A stateful (<c>HttpServerTransportOptions.Stateless = false</c>)
    /// server refuses a sessionless draft request so that a dual-era client downgrades to the legacy
    /// <c>initialize</c> flow. Clients prefer this revision by default and automatically fall back to the
    /// legacy flow when the server does not support it; pin <see cref="McpClientOptions.ProtocolVersion"/>
    /// to a legacy version to opt out, or set <see cref="McpClientOptions.MinProtocolVersion"/> to this
    /// value to keep the draft preference while refusing the legacy fallback.
    /// </remarks>
    public const string DraftProtocolVersion = McpSessionHandler.DraftProtocolVersion;

    /// <summary>Gets an identifier associated with the current MCP session.</summary>
    /// <remarks>
    /// Typically populated in transports supporting multiple sessions, such as Streamable HTTP or SSE.
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
    /// Gets a value indicating whether the negotiated protocol version is the draft revision
    /// (<see cref="DraftProtocolVersion"/>, which carries SEP-2575 + SEP-2567 + MRTR).
    /// </summary>
    /// <remarks>
    /// Returns <see langword="false"/> when no version has been negotiated yet. This is the shared
    /// definition of "is this peer speaking the draft revision" used by both the client and server.
    /// </remarks>
    internal bool IsDraftProtocol() =>
        NegotiatedProtocolVersion == DraftProtocolVersion;

    /// <summary>
    /// Sends a JSON-RPC request to the connected session and waits for a response.
    /// </summary>
    /// <param name="request">The JSON-RPC request to send.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task containing the session's response.</returns>
    /// <exception cref="InvalidOperationException">The transport is not connected, or another error occurred during request processing.</exception>
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
    /// <exception cref="InvalidOperationException">The transport is not connected, or <paramref name="message"/> is a <see cref="JsonRpcRequest"/>. Use <see cref="SendRequestAsync"/> for requests.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This method provides low-level access to send any JSON-RPC message. For specific message types,
    /// consider using the higher-level methods such as <see cref="SendRequestAsync"/> or methods
    /// on this class that provide a simpler API.
    /// </para>
    /// <para>
    /// The method serializes the message and transmits it using the underlying transport mechanism.
    /// </para>
    /// </remarks>
    public abstract Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default);

    /// <summary>Registers a handler to be invoked when a notification for the specified method is received.</summary>
    /// <param name="method">The notification method.</param>
    /// <param name="handler">The handler to be invoked.</param>
    /// <returns>An <see cref="IAsyncDisposable"/> that will remove the registered handler when disposed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> or <paramref name="handler"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="method"/> is empty or composed entirely of whitespace.</exception>
    public abstract IAsyncDisposable RegisterNotificationHandler(string method, Func<JsonRpcNotification, CancellationToken, ValueTask> handler);

    /// <inheritdoc/>
    public abstract ValueTask DisposeAsync();
}
