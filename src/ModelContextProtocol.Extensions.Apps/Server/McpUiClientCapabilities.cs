using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents the MCP Apps capabilities advertised by a client.
/// </summary>
/// <remarks>
/// <para>
/// This object is the value associated with the <c>"io.modelcontextprotocol/ui"</c> key in the
/// <see cref="Protocol.ClientCapabilities.Extensions"/> dictionary.
/// </para>
/// </remarks>
[Experimental(Experimentals.Apps_DiagnosticId, UrlFormat = Experimentals.Apps_Url)]
public sealed class McpUiClientCapabilities
{
    /// <summary>
    /// Gets or sets the list of MIME types supported by the client for MCP App UI resources.
    /// </summary>
    /// <remarks>
    /// A client that supports MCP Apps must include <c>"text/html;profile=mcp-app"</c> in this list.
    /// </remarks>
    [JsonPropertyName("mimeTypes")]
    public IList<string>? MimeTypes { get; set; }
}
