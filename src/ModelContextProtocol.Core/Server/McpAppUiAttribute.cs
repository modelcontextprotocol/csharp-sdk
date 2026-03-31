using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Server;

/// <summary>
/// Specifies MCP Apps UI metadata for a tool method.
/// </summary>
/// <remarks>
/// <para>
/// Apply this attribute alongside <see cref="McpServerToolAttribute"/> to associate a tool with a
/// UI resource in the MCP Apps extension. When processed, it populates both the structured
/// <c>_meta.ui</c> object and the legacy <c>_meta["ui/resourceUri"]</c> flat key in the tool's
/// metadata for backward compatibility with older MCP hosts.
/// </para>
/// <para>
/// This attribute takes precedence over any raw <c>[McpMeta("ui", ...)]</c> attribute on the
/// same method.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp">
/// [McpServerTool]
/// [McpAppUi(ResourceUri = "ui://weather/view.html")]
/// [Description("Get current weather for a location")]
/// public string GetWeather(string location) => ...;
///
/// // Restrict visibility to model only:
/// [McpServerTool]
/// [McpAppUi(ResourceUri = "ui://weather/view.html", Visibility = [McpUiToolVisibility.Model])]
/// public string GetWeatherModelOnly(string location) => ...;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method)]
[Experimental(Experimentals.Apps_DiagnosticId, UrlFormat = Experimentals.Apps_Url)]
public sealed class McpAppUiAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the URI of the UI resource associated with this tool.
    /// </summary>
    /// <remarks>
    /// This should be a <c>ui://</c> URI pointing to the HTML resource registered
    /// with the server (e.g., <c>"ui://weather/view.html"</c>).
    /// </remarks>
    public string? ResourceUri { get; set; }

    /// <summary>
    /// Gets or sets the visibility of the tool, controlling which principals can invoke it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Allowed values are <see cref="McpUiToolVisibility.Model"/> and <see cref="McpUiToolVisibility.App"/>.
    /// When <see langword="null"/> or empty, the tool is visible to both the model and the app (the default).
    /// </para>
    /// </remarks>
    public string[]? Visibility { get; set; }
}
