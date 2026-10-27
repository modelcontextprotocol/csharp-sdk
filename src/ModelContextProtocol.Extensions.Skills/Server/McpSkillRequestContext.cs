using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Describes the request an <see cref="IMcpSkillCatalog"/> is answering.
/// </summary>
/// <remarks>
/// <para>
/// The skills methods are registered as raw request handlers, so they do not pass through the typed request
/// filter pipeline that guards the built-in resource methods (for example, the ASP.NET Core authorization
/// filters). A catalog that must not disclose every skill to every caller makes that decision itself, from the
/// <see cref="MessageContext.User"/> and any <see cref="MessageContext.Items"/> that incoming-message filters attached to the request.
/// </para>
/// <para>
/// <see cref="MessageContext.User"/> is populated by the ASP.NET Core transport from the HTTP request's principal. For other
/// transports, or when no authentication is configured, it is <see langword="null"/>.
/// </para>
/// <para>
/// <see cref="MessageContext.Services"/> provides the services for this request, including any request-scoped
/// dependencies. Use this provider rather than <see cref="McpServer.Services"/>, which may be longer-lived.
/// The request infrastructure owns the scope; catalogs must not dispose it or retain its services after returning.
/// </para>
/// </remarks>
public sealed class McpSkillRequestContext : MessageContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpSkillRequestContext"/> class.
    /// </summary>
    /// <param name="server">The server answering the request.</param>
    /// <param name="jsonRpcRequest">The JSON-RPC request being answered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="server"/> or <paramref name="jsonRpcRequest"/> is <see langword="null"/>.</exception>
    public McpSkillRequestContext(McpServer server, JsonRpcRequest jsonRpcRequest)
        : base(server, jsonRpcRequest)
    {
    }

    /// <summary>
    /// Gets the JSON-RPC request being answered.
    /// </summary>
    public JsonRpcRequest JsonRpcRequest => (JsonRpcRequest)JsonRpcMessage;

    /// <summary>
    /// Gets the protocol version the request was made under, when the transport or the request carried one.
    /// </summary>
    public string? ProtocolVersion => JsonRpcRequest.Context?.ProtocolVersion;
}
