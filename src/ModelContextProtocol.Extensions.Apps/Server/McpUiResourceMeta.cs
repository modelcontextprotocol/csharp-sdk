using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents the UI metadata associated with an MCP resource in the MCP Apps extension.
/// </summary>
/// <remarks>
/// This metadata is placed under the <c>ui</c> key in the resource's <c>_meta</c> object.
/// It provides Content Security Policy (CSP) configuration, sandbox permissions, CORS origin, and
/// visual boundary preferences for the UI resource served by this MCP server.
/// </remarks>
[Experimental(Experimentals.Apps_DiagnosticId, UrlFormat = Experimentals.Apps_Url)]
public sealed class McpUiResourceMeta
{
    /// <summary>
    /// Gets or sets the Content Security Policy configuration for this resource.
    /// </summary>
    /// <remarks>
    /// Specifies the allowed origins for network requests, resource loads, and nested frames.
    /// </remarks>
    [JsonPropertyName("csp")]
    public McpUiResourceCsp? Csp { get; set; }

    /// <summary>
    /// Gets or sets the sandbox permissions for this resource.
    /// </summary>
    /// <remarks>
    /// Controls which browser sandbox features the UI resource is allowed to use.
    /// </remarks>
    [JsonPropertyName("permissions")]
    public McpUiResourcePermissions? Permissions { get; set; }

    /// <summary>
    /// Gets or sets the dedicated origin domain for this resource.
    /// </summary>
    /// <remarks>
    /// When set, the host will serve the resource from this dedicated origin,
    /// enabling OAuth flows and CORS without wildcard exceptions.
    /// </remarks>
    [JsonPropertyName("domain")]
    public string? Domain { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the host should render a visual border around the UI.
    /// </summary>
    [JsonPropertyName("prefersBorder")]
    public bool? PrefersBorder { get; set; }
}
