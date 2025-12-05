using ModelContextProtocol.Server;
using System.Security.Claims;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Contains contextual information for JSON-RPC messages that is not part of the JSON-RPC protocol specification.
/// </summary>
/// <remarks>
/// This class holds transport-specific and runtime context information that accompanies JSON-RPC messages
/// but is not serialized as part of the JSON-RPC payload. This includes transport references, execution context,
/// and authenticated user information.
/// </remarks>
public class JsonRpcMessageContext
{
    /// <summary>
    /// Gets or sets the transport the <see cref="JsonRpcMessage"/> was received on or should be sent over.
    /// </summary>
    /// <remarks>
    /// This property is used to support the Streamable HTTP transport where the specification states that the server
    /// SHOULD include JSON-RPC responses in the HTTP response body for the POST request containing
    /// the corresponding JSON-RPC request. It can be <see langword="null"/> for other transports.
    /// </remarks>
    public ITransport? RelatedTransport { get; set; }

    /// <summary>
    /// Gets or sets the <see cref="ExecutionContext"/> that should be used to run any handlers
    /// </summary>
    /// <remarks>
    /// This property is used to support the Streamable HTTP transport in its default stateful mode. In this mode,
    /// the <see cref="McpServer"/> outlives the initial HTTP request context it was created on, and new
    /// JSON-RPC messages can originate from future HTTP requests. This behavior allows the transport to flow the
    /// context with the JSON-RPC message. This is particularly useful for enabling IHttpContextAccessor
    /// in tool calls.
    /// </remarks>
    public ExecutionContext? ExecutionContext { get; set; }

    /// <summary>
    /// Gets or sets the authenticated user associated with this JSON-RPC message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property contains the <see cref="ClaimsPrincipal"/> representing the authenticated user
    /// who initiated this JSON-RPC message. This enables request handlers to access user identity
    /// and authorization information without requiring dependency on HTTP context accessors
    /// or other HTTP-specific abstractions.
    /// </para>
    /// <para>
    /// The user information is automatically populated by the transport layer when processing
    /// incoming HTTP requests in ASP.NET Core scenarios. For other transport types or scenarios
    /// where user authentication is not applicable, this property can be <see langword="null"/>.
    /// </para>
    /// <para>
    /// This property is particularly useful in the Streamable HTTP transport where JSON-RPC messages
    /// might outlive the original HTTP request context, allowing user identity to be preserved
    /// throughout the message processing pipeline.
    /// </para>
    /// </remarks>
    public ClaimsPrincipal? User { get; set; }

    /// <summary>
    /// Gets or sets a callback that closes the SSE stream associated with the current JSON-RPC request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This callback implements the SSE polling pattern from SEP-1699. When invoked, it gracefully closes
    /// the SSE stream for the current request, signaling to the client that it should reconnect to receive
    /// the response. The server must have sent a priming event with an event ID before this callback is invoked.
    /// </para>
    /// <para>
    /// This is useful for long-running operations where the server wants to free resources while the operation
    /// is in progress. The client will reconnect with the Last-Event-ID header, and the server will replay
    /// any events that were sent after that ID.
    /// </para>
    /// <para>
    /// This callback is only available when using the Streamable HTTP transport with resumability enabled.
    /// For other transports, this property will be <see langword="null"/>.
    /// </para>
    /// </remarks>
    public Action? CloseSseStream { get; set; }

    /// <summary>
    /// Gets or sets a callback that closes the standalone SSE stream for the current session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This callback closes the standalone SSE stream that is used for server-initiated messages
    /// (notifications and requests from server to client). Unlike <see cref="CloseSseStream"/>,
    /// this affects the session-level SSE stream rather than a request-specific stream.
    /// </para>
    /// <para>
    /// This is useful when the server needs to signal the client to reconnect its standalone SSE stream,
    /// for example during server restarts or resource cleanup.
    /// </para>
    /// <para>
    /// This callback is only available when using the Streamable HTTP transport.
    /// For other transports, this property will be <see langword="null"/>.
    /// </para>
    /// </remarks>
    public Action? CloseStandaloneSseStream { get; set; }
}
