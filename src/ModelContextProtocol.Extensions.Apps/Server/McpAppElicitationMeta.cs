using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Apps;

/// <summary>Associates a form elicitation with the MCP App that should render it.</summary>
[Experimental(Experimentals.Apps_DiagnosticId, UrlFormat = Experimentals.Apps_Url)]
public sealed class McpAppElicitationMeta
{
    /// <summary>Gets or sets the <c>ui://</c> resource URI for the elicitation UI.</summary>
    [JsonPropertyName("resourceUri")]
    public string ResourceUri { get; set; } = string.Empty;
}
