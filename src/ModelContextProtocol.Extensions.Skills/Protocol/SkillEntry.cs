using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Represents a single skill as returned by <c>skills/list</c> and <c>skills/get</c>.
/// </summary>
/// <remarks>
/// <para>
/// An entry is a complete, point-in-time snapshot of a skill: its <c>SKILL.md</c> URI, the verbatim
/// frontmatter of that file, and the manifest of the skill's files. A host that pages through a listing
/// therefore has everything it needs to build its registry and verify every file it later reads, without
/// a second round-trip per skill.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/pull/2640">SEP-2640</see>
/// specification for details.
/// </para>
/// </remarks>
public sealed class SkillEntry
{
    /// <summary>
    /// Gets or sets the resource URI of the skill's <c>SKILL.md</c>.
    /// </summary>
    /// <remarks>
    /// The final path segment preceding <c>/SKILL.md</c> must equal the <c>name</c> field of
    /// <see cref="Frontmatter"/>, so that a skill's name is recoverable from its URI alone.
    /// </remarks>
    [JsonPropertyName("uri")]
    public required string Uri { get; set; }

    /// <summary>
    /// Gets or sets the skill's <c>SKILL.md</c> YAML frontmatter rendered verbatim as a JSON object.
    /// </summary>
    /// <remarks>
    /// Every field the author wrote is passed through, not a curated subset. Hosts re-parse the fetched
    /// <c>SKILL.md</c> and compare it against this object field by field, treating any discrepancy as a
    /// verification failure, so this must reproduce the authored frontmatter exactly.
    /// </remarks>
    [JsonPropertyName("frontmatter")]
    public required JsonObject Frontmatter { get; set; }

    /// <summary>
    /// Gets or sets the skill's file manifest.
    /// </summary>
    [JsonPropertyName("resources")]
    public required SkillResources Resources { get; set; }
}
