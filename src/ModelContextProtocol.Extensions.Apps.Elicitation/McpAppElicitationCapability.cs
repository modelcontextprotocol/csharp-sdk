using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Apps.Elicitation;

/// <summary>Describes support for rendering form elicitations with MCP Apps.</summary>
public sealed class McpAppElicitationCapability
{
    /// <summary>Gets the extensions that must also be negotiated.</summary>
    [JsonPropertyName("requires")]
    public IList<string> Requires { get; set; } = [McpApps.ExtensionId];
}
