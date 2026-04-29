using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents the sandbox permissions requested by an MCP Apps UI resource.
/// </summary>
/// <remarks>
/// This maps to the <c>allow</c> attribute on the iframe sandbox in the MCP host.
/// Permissions are specified as standard browser iframe permission strings,
/// such as <c>"camera"</c>, <c>"microphone"</c>, or <c>"geolocation"</c>.
/// </remarks>
[Experimental(Experimentals.Apps_DiagnosticId, UrlFormat = Experimentals.Apps_Url)]
public sealed class McpUiResourcePermissions
{
    /// <summary>
    /// Gets or sets the list of permissions granted to the sandboxed UI resource.
    /// </summary>
    /// <remarks>
    /// These correspond to values allowed in the <c>allow</c> attribute of an HTML iframe,
    /// for example <c>"camera"</c>, <c>"microphone"</c>, <c>"geolocation"</c>,
    /// <c>"clipboard-read"</c>, or <c>"clipboard-write"</c>.
    /// </remarks>
    [JsonPropertyName("allow")]
    public IList<string>? Allow { get; set; }
}
