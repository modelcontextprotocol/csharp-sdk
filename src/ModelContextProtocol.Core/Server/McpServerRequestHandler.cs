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
/// The handler receives the raw <see cref="JsonRpcRequest"/> and returns a serialized
/// <see cref="JsonNode"/> response, giving extensions full control over request/response serialization.
/// </para>
/// </remarks>
[Experimental(Experimentals.Subclassing_DiagnosticId, UrlFormat = Experimentals.Subclassing_Url)]
public sealed class McpServerRequestHandler
{
    /// <summary>
    /// Gets the JSON-RPC method name this handler responds to.
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// Gets the handler function that processes incoming requests for the specified method.
    /// </summary>
    /// <remarks>
    /// The handler receives the full <see cref="JsonRpcRequest"/> and a <see cref="CancellationToken"/>,
    /// and returns a serialized <see cref="JsonNode"/> response (or <see langword="null"/> for void methods).
    /// </remarks>
    public required Func<JsonRpcRequest, CancellationToken, ValueTask<JsonNode?>> Handler { get; init; }
}
