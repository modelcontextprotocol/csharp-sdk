using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Represents a skill's file manifest: either a complete enumeration of the skill's files, or the
/// <c>"dynamic"</c> marker indicating that the skill's content is generated and cannot be digested.
/// </summary>
/// <remarks>
/// <para>
/// The specification requires this value on every skill entry and admits exactly two forms. An entry with no
/// manifest at all, or with any value other than an array or the string <c>"dynamic"</c>, is invalid and hosts
/// must not load it. Use <see cref="FromResources"/> or <see cref="Dynamic"/> to construct one; there is
/// deliberately no public constructor, so an invalid manifest cannot be produced by accident.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/ext-skills/blob/main/specification/stable/skills.mdx">Skills extension specification</see>
/// for details.
/// </para>
/// </remarks>
[JsonConverter(typeof(SkillResourcesConverter))]
public sealed class SkillResources
{
    private SkillResources(IReadOnlyList<SkillResource>? resources) => Resources = resources;

    /// <summary>
    /// Gets a manifest representing a skill whose content is generated dynamically.
    /// </summary>
    /// <remarks>
    /// A skill declared this way offers no content integrity and cannot be content-bound. Hosts may decline
    /// to load it, and server authors should expect that some will.
    /// </remarks>
    public static SkillResources Dynamic { get; } = new(null);

    /// <summary>
    /// Creates a manifest enumerating every file of a skill.
    /// </summary>
    /// <param name="resources">
    /// The skill's complete file list. It must include an entry whose URI equals the skill's own
    /// <see cref="Skill.Uri"/>, carrying the digest and size of the <c>SKILL.md</c> itself.
    /// </param>
    /// <returns>A manifest wrapping a copy of <paramref name="resources"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resources"/> is <see langword="null"/>.</exception>
    public static SkillResources FromResources(IEnumerable<SkillResource> resources)
    {
#if NET
        ArgumentNullException.ThrowIfNull(resources);
#else
        if (resources is null) throw new ArgumentNullException(nameof(resources));
#endif

        return new SkillResources(resources.ToArray());
    }

    /// <summary>
    /// Gets a value indicating whether this manifest is the <c>"dynamic"</c> marker.
    /// </summary>
    public bool IsDynamic => Resources is null;

    /// <summary>
    /// Gets the enumerated files, or <see langword="null"/> when <see cref="IsDynamic"/> is <see langword="true"/>.
    /// </summary>
    public IReadOnlyList<SkillResource>? Resources { get; }
}
