using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Represents the entry for a single skill, as returned by <c>skills/list</c> and <c>skills/get</c>.
/// </summary>
/// <remarks>
/// <para>
/// An entry is a complete, point-in-time snapshot of a skill: the URI of its <c>SKILL.md</c>, the verbatim
/// frontmatter of that file, and a manifest of the skill's files with their digests and sizes. A host that
/// pages through a listing has everything it needs to build its registry, present a skill for approval, and
/// verify every file it later reads, without a second round-trip per skill.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/ext-skills/blob/main/specification/stable/skills.mdx">Skills extension specification</see>
/// for details.
/// </para>
/// </remarks>
public sealed class Skill
{
    /// <summary>
    /// Gets or sets the resource URI of the skill's <c>SKILL.md</c>, readable via <c>resources/read</c>.
    /// </summary>
    /// <remarks>
    /// The path segment preceding <c>/SKILL.md</c> must equal the <c>name</c> field of <see cref="Frontmatter"/>,
    /// so that a skill's name is recoverable from its URI alone.
    /// </remarks>
    [JsonPropertyName("uri")]
    public required string Uri { get; set; }

    /// <summary>
    /// Gets or sets the skill's <c>SKILL.md</c> YAML frontmatter, rendered verbatim as a JSON object.
    /// </summary>
    /// <remarks>
    /// Every field the author wrote is passed through, not a curated subset. <c>name</c> and <c>description</c>
    /// are always present. Hosts re-parse the fetched <c>SKILL.md</c> and compare its frontmatter against this
    /// object field by field, treating any discrepancy as a verification failure, so this must reproduce the
    /// authored frontmatter exactly.
    /// </remarks>
    [JsonPropertyName("frontmatter")]
    public required JsonObject Frontmatter { get; set; }

    /// <summary>
    /// Gets or sets the skill's file manifest: an enumeration of every file with its digest and size, or
    /// <see cref="SkillResources.Dynamic"/> when the skill's content is generated and cannot be digested.
    /// </summary>
    [JsonPropertyName("resources")]
    public required SkillResources Resources { get; set; }

    /// <summary>
    /// Gets the skill's name from <see cref="Frontmatter"/>, or <see langword="null"/> if it is absent or not a string.
    /// </summary>
    /// <remarks>
    /// A skill's name is a label, not an identifier. Skills are identified by <see cref="Uri"/> within a server,
    /// and by the pair of server identity and <see cref="Uri"/> across servers.
    /// </remarks>
    [JsonIgnore]
    public string? Name => GetFrontmatterString("name");

    /// <summary>
    /// Gets the skill's description from <see cref="Frontmatter"/>, or <see langword="null"/> if it is absent or not a string.
    /// </summary>
    [JsonIgnore]
    public string? Description => GetFrontmatterString("description");

    private string? GetFrontmatterString(string key) =>
        Frontmatter.TryGetPropertyValue(key, out var node) &&
        node is JsonValue value &&
        value.TryGetValue(out string? text)
            ? text
            : null;
}
