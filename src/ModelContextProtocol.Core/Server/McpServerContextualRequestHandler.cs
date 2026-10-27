using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents a custom JSON-RPC request handler with access to the request's server and services.
/// </summary>
/// <remarks>
/// <para>
/// Register handlers through <see cref="McpServerOptions.ContextualRequestHandlers"/>. The handler controls
/// request/response serialization and does not pass through the typed request filters.
/// </para>
/// <para>
/// The service scope lifecycle is the same as for built-in request handlers, honoring
/// <see cref="McpServerOptions.ScopeRequests"/> and asynchronously disposing any scope the server creates.
/// Use <see cref="MessageContext.Services"/> to resolve request-scoped services; the provider on
/// <see cref="MessageContext.Server"/> may be the server's longer-lived provider.
/// </para>
/// </remarks>
[Experimental(Experimentals.Extensibility_DiagnosticId, UrlFormat = Experimentals.Extensibility_Url)]
public sealed class McpServerContextualRequestHandler
{
    /// <summary>
    /// Gets the JSON-RPC method name this handler responds to.
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// Gets the name of the top-level request parameter whose value is mirrored in the
    /// <c>Mcp-Name</c> HTTP routing header.
    /// </summary>
    /// <remarks>
    /// When set, Streamable HTTP servers require the request to include an <c>Mcp-Name</c>
    /// header whose decoded value matches the string value of this parameter.
    /// </remarks>
    public string? RoutingNameParameter { get; init; }

    /// <summary>
    /// Gets the handler function that processes incoming requests for the specified method.
    /// </summary>
    /// <remarks>
    /// The handler receives the raw JSON parameters in <see cref="RequestContext{TParams}.Params"/> and the
    /// complete request in <see cref="RequestContext{TParams}.JsonRpcRequest"/>. It returns a serialized
    /// <see cref="JsonNode"/> response, or <see langword="null"/> for void methods.
    /// </remarks>
    public required McpRequestHandler<JsonNode?, JsonNode?> Handler { get; init; }
}
