using ModelContextProtocol.Server;
using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Extensions.Apps;

/// <summary>
/// Specifies MCP Apps UI metadata for a resource method.
/// </summary>
/// <remarks>
/// <para>
/// Apply this attribute alongside <see cref="McpServerResourceAttribute"/> to configure the
/// Content Security Policy, sandbox permissions, domain, and visual preferences for an MCP App
/// resource. When processed by <see cref="McpApps.ApplyAppResourceAttributes(McpServerResource)"/>,
/// it populates the structured <c>_meta.ui</c> object in the resource's metadata.
/// </para>
/// <para>
/// Explicit <c>Meta["ui"]</c> set via <see cref="McpServerResourceCreateOptions"/> or a raw
/// <c>[McpMeta("ui", ...)]</c> attribute takes precedence over this attribute.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp">
/// [McpServerResource(UriTemplate = "ui://weather/view.html", MimeType = McpApps.HtmlMimeType)]
/// [McpAppResource(ConnectDomains = ["https://api.weather.gov"], PrefersBorder = true)]
/// public static string GetWeatherUi() =&gt; ...;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method)]
[Experimental(Experimentals.Apps_DiagnosticId, UrlFormat = Experimentals.Apps_Url)]
public sealed class McpAppResourceAttribute : Attribute
{
    private bool? _prefersBorder;

    /// <summary>Gets or sets origins allowed for network connections.</summary>
    public string[]? ConnectDomains { get; set; }

    /// <summary>Gets or sets origins allowed for scripts, stylesheets, images, and fonts.</summary>
    public string[]? ResourceDomains { get; set; }

    /// <summary>Gets or sets origins allowed for nested frames.</summary>
    public string[]? FrameDomains { get; set; }

    /// <summary>Gets or sets allowed base URIs.</summary>
    public string[]? BaseUris { get; set; }

    /// <summary>Gets or sets browser permissions requested by the sandboxed resource.</summary>
    public string[]? Permissions { get; set; }

    /// <summary>Gets or sets the dedicated origin domain for this resource.</summary>
    public string? Domain { get; set; }

    /// <summary>Gets or sets whether the host should render a visual border around the UI.</summary>
    public bool PrefersBorder
    {
        get => _prefersBorder.GetValueOrDefault();
        set => _prefersBorder = value;
    }

    internal bool? PrefersBorderValue => _prefersBorder;
}
