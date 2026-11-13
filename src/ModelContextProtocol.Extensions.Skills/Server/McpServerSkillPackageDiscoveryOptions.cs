using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>Provides common settings for authoring packages from immediate child skill directories.</summary>
[Experimental(Experimentals.Skills_DiagnosticId, UrlFormat = Experimentals.Skills_Url)]
public sealed class McpServerSkillPackageDiscoveryOptions
{
    /// <summary>Gets or sets the prefix used to derive each skill's URI from its frontmatter name.</summary>
    /// <remarks>
    /// Defaults to <c>skill://</c>. A missing trailing slash is added to a local copy.
    /// Names used as URI path segments are escaped with <see cref="Uri.EscapeDataString(string)"/>.
    /// Each discovered skill reads its own frontmatter.
    /// </remarks>
    public string UriPrefix { get; set; } = "skill://";

    /// <summary>Gets or sets whether dot-prefixed, hidden, and system directories and files are included.</summary>
    /// <remarks>The default is false. Included symbolic links remain errors.</remarks>
    public bool IncludeHiddenAndSystemFiles { get; set; }

    /// <summary>Gets or sets common server-local metadata applied to every authored skill.</summary>
    /// <remarks>
    /// Metadata is not copied to resources and does not authorize requests automatically.
    /// </remarks>
    public IReadOnlyList<object>? Metadata { get; set; }

    /// <summary>Gets or sets common entry-level protocol metadata applied to every authored skill.</summary>
    /// <remarks>File resource metadata is unchanged.</remarks>
    public JsonObject? Meta { get; set; }
}
