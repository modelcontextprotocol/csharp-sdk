using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Apps.Elicitation;

/// <summary>Associates a form elicitation with the MCP App that should render it.</summary>
public sealed class McpAppElicitationMeta
{
    /// <summary>Gets or sets the <c>ui://</c> resource URI for the elicitation UI.</summary>
    [JsonPropertyName("resourceUri")]
    public required string ResourceUri { get; set; }
}
