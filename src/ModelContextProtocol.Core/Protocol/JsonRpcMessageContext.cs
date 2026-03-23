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
public sealed class JsonRpcMessageContext
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
    /// Gets or sets the <see cref="ExecutionContext"/> that should be used to run any handlers.
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
    /// Gets or sets a key/value collection that can be used to share data within the scope of this message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property allows data to be flowed throughout the message processing pipeline,
    /// including from incoming message filters to request-specific filters and handlers.
    /// </para>
    /// <para>
    /// When creating a <see cref="MessageContext"/> or <see cref="RequestContext{TParams}"/> for server-side
    /// processing, the Items dictionary from this context will be used, ensuring data set in message filters
    /// is available in request filters and handlers.
    /// </para>
    /// </remarks>
    public IDictionary<string, object?>? Items { get; set; }

    /// <summary>
    /// Gets or sets the protocol version from the transport-level header (e.g. <c>Mcp-Protocol-Version</c>)
    /// that accompanied this JSON-RPC message.
    /// </summary>
    /// <remarks>
    /// In stateless Streamable HTTP mode, the protocol version cannot be negotiated via the <c>initialize</c>
    /// handshake because each request creates a new server instance. This property allows the transport layer
    /// to flow the protocol version header so the server can determine client capabilities.
    /// </remarks>
    public string? ProtocolVersion { get; set; }

    /// <summary>
    /// Gets or sets the MRTR context for this request, if any.
    /// </summary>
    /// <remarks>
    /// Set by <see cref="McpServer"/> when an MRTR-aware handler invocation is in progress,
    /// so that the per-request <see cref="DestinationBoundMcpServer"/> can intercept
    /// server-to-client requests (e.g. ElicitAsync, SampleAsync) and route them through the MRTR mechanism.
    /// </remarks>
    internal MrtrContext? MrtrContext { get; set; }
}
