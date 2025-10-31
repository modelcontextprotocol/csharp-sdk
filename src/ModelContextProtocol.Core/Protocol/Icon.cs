using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents an icon that can be used to visually identify an implementation, resource, tool, or prompt.
/// </summary>
/// <remarks>
/// <para>
/// Icons enhance user interfaces by providing visual context and improving the discoverability of available functionality.
/// Each icon includes a source URI pointing to the icon resource, and optional MIME type and size information.
/// </para>
/// <para>
/// Clients that support rendering icons MUST support at least the following MIME types:
/// </para>
/// <list type="bullet">
/// <item><description>image/png - PNG images (safe, universal compatibility)</description></item>
/// <item><description>image/jpeg (and image/jpg) - JPEG images (safe, universal compatibility)</description></item>
/// </list>
/// <para>
/// Clients that support rendering icons SHOULD also support:
/// </para>
/// <list type="bullet">
/// <item><description>image/svg+xml - SVG images (scalable but requires security precautions)</description></item>
/// <item><description>image/webp - WebP images (modern, efficient format)</description></item>
/// </list>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class Icon
{
    /// <summary>
    /// Gets or sets the URI pointing to the icon resource.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This can be an HTTP/HTTPS URL pointing to an image file or a data URI with base64-encoded image data.
    /// </para>
    /// <para>
    /// Consumers SHOULD take steps to ensure URLs serving icons are from the same domain as the client/server 
    /// or a trusted domain.
    /// </para>
    /// <para>
    /// Consumers SHOULD take appropriate precautions when consuming SVGs as they can contain executable JavaScript.
    /// </para>
    /// </remarks>
    [JsonPropertyName("src")]
    public required string Source { get; set; }

    /// <summary>
    /// Gets or sets the optional MIME type of the icon.
    /// </summary>
    /// <remarks>
    /// This can be used to override the server's MIME type if it's missing or generic.
    /// Common values include "image/png", "image/jpeg", "image/svg+xml", and "image/webp".
    /// </remarks>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>
    /// Gets or sets the optional size specifications for the icon.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This can specify one or more sizes at which the icon file can be used.
    /// Examples include "48x48", "any" for scalable formats like SVG.
    /// </para>
    /// <para>
    /// If not provided, clients should assume that the icon can be used at any size.
    /// </para>
    /// </remarks>
    [JsonPropertyName("sizes")]
    public IList<string>? Sizes { get; set; }

    /// <summary>
    /// Gets or sets the optional theme for this icon.
    /// </summary>
    /// <remarks>
    /// Can be "light", "dark", or a custom theme identifier.
    /// Used to specify which UI theme the icon is designed for.
    /// </remarks>
    [JsonPropertyName("theme")]
    public string? Theme { get; set; }
}
