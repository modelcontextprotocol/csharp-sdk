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
    /// This is used to support the Streamable HTTP transport where the specification states that the server
    /// SHOULD include JSON-RPC responses in the HTTP response body for the POST request containing
    /// the corresponding JSON-RPC request. It may be <see langword="null"/> for other transports.
    /// </remarks>
    public ITransport? RelatedTransport { get; set; }

    /// <summary>
    /// Gets or sets the <see cref="ExecutionContext"/> that should be used to run any handlers
    /// </summary>
    /// <remarks>
    /// This is used to support the Streamable HTTP transport in its default stateful mode. In this mode,
    /// the <see cref="McpServer"/> outlives the initial HTTP request context it was created on, and new
    /// JSON-RPC messages can originate from future HTTP requests. This allows the transport to flow the
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
    /// where user authentication is not applicable, this property may be <see langword="null"/>.
    /// </para>
    /// <para>
    /// This property is particularly useful in the Streamable HTTP transport where JSON-RPC messages
    /// may outlive the original HTTP request context, allowing user identity to be preserved
    /// throughout the message processing pipeline.
    /// </para>
    /// </remarks>
    public ClaimsPrincipal? User { get; set; }
}
