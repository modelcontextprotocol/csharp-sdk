using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents the Content Security Policy (CSP) domain allowlists for an MCP Apps UI resource.
/// </summary>
/// <remarks>
/// <para>
/// These allowlists are used by the MCP host to construct the Content-Security-Policy HTTP header
/// for the sandboxed iframe that hosts the UI resource.
/// </para>
/// <para>
/// Each list contains origins (e.g., <c>"https://api.example.com"</c>) that are permitted for
/// the corresponding CSP directive.
/// </para>
/// </remarks>
[Experimental(Experimentals.Apps_DiagnosticId, UrlFormat = Experimentals.Apps_Url)]
public sealed class McpUiResourceCsp
{
    /// <summary>
    /// Gets or sets the list of origins allowed for fetch, XMLHttpRequest, WebSocket, and EventSource
    /// connections (<c>connect-src</c> CSP directive).
    /// </summary>
    [JsonPropertyName("connectDomains")]
    public IList<string>? ConnectDomains { get; set; }

    /// <summary>
    /// Gets or sets the list of origins allowed for loading scripts, stylesheets, images, and fonts
    /// (<c>script-src</c>, <c>style-src</c>, <c>img-src</c>, <c>font-src</c> CSP directives).
    /// </summary>
    [JsonPropertyName("resourceDomains")]
    public IList<string>? ResourceDomains { get; set; }

    /// <summary>
    /// Gets or sets the list of origins allowed for loading nested frames
    /// (<c>frame-src</c> CSP directive).
    /// </summary>
    [JsonPropertyName("frameDomains")]
    public IList<string>? FrameDomains { get; set; }

    /// <summary>
    /// Gets or sets the list of allowed base URIs
    /// (<c>base-uri</c> CSP directive).
    /// </summary>
    [JsonPropertyName("baseUris")]
    public IList<string>? BaseUris { get; set; }
}
