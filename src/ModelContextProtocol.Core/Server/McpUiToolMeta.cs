using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents the UI metadata associated with an MCP tool in the MCP Apps extension.
/// </summary>
/// <remarks>
/// <para>
/// This metadata is placed under the <c>ui</c> key in the tool's <c>_meta</c> object.
/// It associates the tool with a UI resource (identified by a <c>ui://</c> URI) and optionally
/// controls which principals (model, app) can call the tool.
/// </para>
/// </remarks>
[Experimental(Experimentals.Apps_DiagnosticId, UrlFormat = Experimentals.Apps_Url)]
public sealed class McpUiToolMeta
{
    /// <summary>
    /// Gets or sets the URI of the UI resource associated with this tool.
    /// </summary>
    /// <remarks>
    /// This should be a <c>ui://</c> URI pointing to the HTML resource registered
    /// with the server (e.g., <c>"ui://weather/view.html"</c>).
    /// </remarks>
    [JsonPropertyName("resourceUri")]
    public string? ResourceUri { get; set; }

    /// <summary>
    /// Gets or sets the visibility of the tool, controlling which principals can invoke it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Allowed values are <see cref="McpUiToolVisibility.Model"/> (<c>"model"</c>) and
    /// <see cref="McpUiToolVisibility.App"/> (<c>"app"</c>). When <see langword="null"/>
    /// or empty, the tool is visible to both the model and the app (the default).
    /// </para>
    /// </remarks>
    [JsonPropertyName("visibility")]
    public IList<string>? Visibility { get; set; }
}
