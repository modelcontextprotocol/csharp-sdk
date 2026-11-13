using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents a custom request handler that can be registered with the MCP server to handle
/// arbitrary JSON-RPC methods.
/// </summary>
/// <remarks>
/// <para>
/// Custom request handlers are registered via <see cref="McpServerOptions.RequestHandlers"/> and
/// are invoked when a JSON-RPC request with the matching <see cref="Method"/> is received.
/// The handler receives a <see cref="RequestContext{TParams}"/> with raw JSON parameters and returns a
/// serialized <see cref="JsonNode"/> response, giving extensions full control over request/response serialization.
/// </para>
/// <para>
/// Custom handlers use the same service scope lifecycle as built-in request handlers, honoring
/// <see cref="McpServerOptions.ScopeRequests"/> and asynchronously disposing any scope the server creates.
/// Use <see cref="MessageContext.Services"/> to resolve request-scoped services; the provider on
/// <see cref="MessageContext.Server"/> may be the server's longer-lived provider.
/// Custom handlers do not pass through the typed request filters.
/// </para>
/// </remarks>
[Experimental(Experimentals.Extensibility_DiagnosticId, UrlFormat = Experimentals.Extensibility_Url)]
public sealed class McpServerRequestHandler
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
    [Experimental(Experimentals.Extensibility_DiagnosticId, UrlFormat = Experimentals.Extensibility_Url)]
    public string? RoutingNameParameter { get; init; }

    /// <summary>
    /// Gets the handler function that processes incoming requests for the specified method.
    /// </summary>
    /// <remarks>
    /// The handler receives the raw JSON parameters in <see cref="RequestContext{TParams}.Params"/> and the
    /// full <see cref="JsonRpcRequest"/> in <see cref="RequestContext{TParams}.JsonRpcRequest"/>.
    /// Composed handlers may replace <see cref="RequestContext{TParams}.Params"/> without changing the
    /// original request, so read operation parameters from the context rather than from the raw request.
    /// It returns a serialized <see cref="JsonNode"/> response, or <see langword="null"/> for void methods.
    /// </remarks>
    public required McpRequestHandler<JsonNode?, JsonNode?> Handler { get; init; }
}
