using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Apps;

/// <summary>Represents the MCP Apps capabilities advertised by a server.</summary>
[Experimental(Experimentals.Apps_DiagnosticId, UrlFormat = Experimentals.Apps_Url)]
public sealed class McpUiServerCapabilities
{
    /// <summary>
    /// Gets or sets the capability indicating that the server may associate a
    /// form elicitation with an MCP App resource.
    /// </summary>
    [JsonPropertyName("elicitation")]
    public McpUiElicitationCapability? Elicitation { get; set; }
}
