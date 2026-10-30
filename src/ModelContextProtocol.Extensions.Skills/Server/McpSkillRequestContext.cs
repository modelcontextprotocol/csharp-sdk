using ModelContextProtocol.Protocol;
using System.Security.Claims;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Describes the request an <see cref="IMcpSkillCatalog"/> is answering.
/// </summary>
/// <remarks>
/// <para>
/// The skills methods are registered as raw request handlers, so they do not pass through the typed request
/// filter pipeline that guards the built-in resource methods (for example, the ASP.NET Core authorization
/// filters). A catalog that must not disclose every skill to every caller makes that decision itself, from the
/// <see cref="User"/> and any <see cref="Items"/> that incoming-message filters attached to the request.
/// </para>
/// <para>
/// <see cref="User"/> is populated by the ASP.NET Core transport from the HTTP request's principal. For other
/// transports, or when no authentication is configured, it is <see langword="null"/>.
/// </para>
/// </remarks>
public sealed class McpSkillRequestContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpSkillRequestContext"/> class.
    /// </summary>
    /// <param name="jsonRpcRequest">The JSON-RPC request being answered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="jsonRpcRequest"/> is <see langword="null"/>.</exception>
    public McpSkillRequestContext(JsonRpcRequest jsonRpcRequest)
    {
#if NET
        ArgumentNullException.ThrowIfNull(jsonRpcRequest);
#else
        if (jsonRpcRequest is null) throw new ArgumentNullException(nameof(jsonRpcRequest));
#endif

        JsonRpcRequest = jsonRpcRequest;
    }

    /// <summary>
    /// Gets the JSON-RPC request being answered.
    /// </summary>
    public JsonRpcRequest JsonRpcRequest { get; }

    /// <summary>
    /// Gets the authenticated user making the request, or <see langword="null"/> if none is associated with it.
    /// </summary>
    public ClaimsPrincipal? User => JsonRpcRequest.Context?.User;

    /// <summary>
    /// Gets the per-request items that incoming-message filters attached to the request, if any.
    /// </summary>
    public IDictionary<string, object?>? Items => JsonRpcRequest.Context?.Items;

    /// <summary>
    /// Gets the protocol version the request was made under, when the transport or the request carried one.
    /// </summary>
    public string? ProtocolVersion => JsonRpcRequest.Context?.ProtocolVersion;
}
