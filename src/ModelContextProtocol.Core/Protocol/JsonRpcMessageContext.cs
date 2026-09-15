using ModelContextProtocol.Server;
using System.Diagnostics.CodeAnalysis;
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
    /// Gets or sets the routing name for this message.
    /// </summary>
    /// <remarks>
    /// Streamable HTTP transports emit this value in the <c>Mcp-Name</c> header. This enables
    /// extension methods to identify the named resource targeted by a request.
    /// </remarks>
    [Experimental(Experimentals.Extensibility_DiagnosticId, UrlFormat = Experimentals.Extensibility_Url)]
    [JsonIgnore]
    public string? RoutingName { get; set; }

    /// <summary>
    /// Gets or sets the authoritative protocol version for this JSON-RPC message.
    /// </summary>
    /// <remarks>
    /// The transport may populate this from a header such as <c>Mcp-Protocol-Version</c>. For modern revisions,
    /// the server validates and projects the matching per-request <c>_meta</c> value. Under an established legacy
    /// revision, future reserved metadata remains opaque and does not establish or change the negotiated session version.
    /// </remarks>
    public string? ProtocolVersion { get; set; }

    /// <summary>
    /// Gets or sets the client info derived from the per-request
    /// <c>_meta/io.modelcontextprotocol/clientInfo</c> field.
    /// </summary>
    /// <remarks>
    /// Introduced by the 2026-07-28 protocol revision (SEP-2575). When the request was made under the 2026-07-28 or later revision,
    /// the server uses this in lieu of the value previously captured during the <c>initialize</c> handshake. Future reserved
    /// metadata remains opaque under legacy revisions, which continue to use their initialized identity.
    /// </remarks>
    public Implementation? ClientInfo { get; set; }

    /// <summary>
    /// Gets or sets the client capabilities derived from the per-request
    /// <c>_meta/io.modelcontextprotocol/clientCapabilities</c> field.
    /// </summary>
    /// <remarks>
    /// Introduced by the 2026-07-28 protocol revision (SEP-2575). Per the spec, the server MUST NOT infer client
    /// capabilities from previous modern requests; the authoritative value is the one declared on each request.
    /// Future reserved metadata remains opaque under legacy revisions, which continue to use the capabilities
    /// negotiated during initialization.
    /// </remarks>
    public ClientCapabilities? ClientCapabilities { get; set; }

    /// <summary>
    /// Gets or sets the per-request log level derived from the
    /// <c>_meta/io.modelcontextprotocol/logLevel</c> field.
    /// </summary>
    /// <remarks>
    /// Introduced by the 2026-07-28 protocol revision (SEP-2575). Replaces the legacy
    /// <see cref="RequestMethods.LoggingSetLevel"/> RPC. When absent from a modern request, the server MUST NOT emit
    /// log notifications for the request. Legacy requests continue to use their negotiated logging behavior.
    /// </remarks>
    public LoggingLevel? LogLevel { get; set; }
}
