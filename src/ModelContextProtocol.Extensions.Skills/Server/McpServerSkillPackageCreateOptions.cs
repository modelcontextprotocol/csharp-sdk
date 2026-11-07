using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>Provides options for authoring a <see cref="McpServerSkillPackage"/> from files or a directory.</summary>
[Experimental(Experimentals.Skills_DiagnosticId, UrlFormat = Experimentals.Skills_Url)]
public sealed class McpServerSkillPackageCreateOptions
{
    /// <summary>Gets or sets the resource URI of the skill's <c>SKILL.md</c>.</summary>
    /// <remarks>
    /// When null, the URI is derived from the frontmatter name as <c>skill://{name}/SKILL.md</c>.
    /// An explicit URI must have a name segment matching the frontmatter.
    /// </remarks>
    public string? Uri { get; set; }

    /// <summary>Gets or sets explicitly supplied JSON frontmatter matching the authored <c>SKILL.md</c>.</summary>
    /// <remarks>
    /// <para>
    /// When null, the SDK reads the frontmatter with its bounded YAML reader. Supply this property for YAML
    /// the reader does not support, after parsing the entire block with a full YAML parser.
    /// </para>
    /// <para>
    /// The SDK still rejects invalid UTF-8, missing frontmatter boundaries, and detected format errors.
    /// When the reader parses the whole block, the supplied object must match it exactly. When it stops at an
    /// unsupported construct, acceptance proves neither whole-document validity nor equality with the file.
    /// The application owns that agreement.
    /// </para>
    /// </remarks>
    public JsonObject? Frontmatter { get; set; }

    /// <summary>Gets or sets whether directory authoring includes dot-prefixed, hidden, and system entries.</summary>
    /// <remarks>
    /// The default is false. Included symbolic links remain errors. This setting applies only to directory
    /// loading; explicit file authoring keeps the files selected by the caller.
    /// </remarks>
    public bool IncludeHiddenAndSystemFiles { get; set; }

    /// <summary>Gets or sets server-local primitive metadata, which is not serialized.</summary>
    /// <remarks>
    /// Metadata is not copied to the file resources and does not automatically authorize any request.
    /// </remarks>
    public IReadOnlyList<object>? Metadata { get; set; }

    /// <summary>Gets or sets the entry-level protocol metadata used to populate <see cref="Skill.Meta"/>.</summary>
    /// <remarks>
    /// This JSON is separate from the frontmatter's string-to-string metadata map
    /// and is not copied to the file resources.
    /// </remarks>
    public JsonObject? Meta { get; set; }

    internal McpServerSkillPackageCreateOptions Clone() => new()
    {
        Uri = Uri,
        Frontmatter = Frontmatter,
        IncludeHiddenAndSystemFiles = IncludeHiddenAndSystemFiles,
        Metadata = Metadata,
        Meta = Meta,
    };
}
