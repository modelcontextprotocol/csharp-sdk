using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Represents a skill's file manifest: either a complete enumeration of the skill's files, or the
/// <c>"dynamic"</c> marker indicating the skill's content is generated and cannot be digested.
/// </summary>
/// <remarks>
/// <para>
/// SEP-2640 requires this value on every skill entry and admits exactly two forms. An entry with no
/// manifest at all, or with any value other than an array or <c>"dynamic"</c>, is invalid and hosts must
/// not load it. Use <see cref="FromResources"/> or <see cref="Dynamic"/> to construct one; there is
/// deliberately no public constructor, so an invalid manifest cannot be produced by accident.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/pull/2640">SEP-2640</see>
/// specification for details.
/// </para>
/// </remarks>
[JsonConverter(typeof(SkillResourcesConverter))]
public sealed class SkillResources
{
    private readonly IReadOnlyList<SkillResource>? _resources;

    private SkillResources(IReadOnlyList<SkillResource>? resources) => _resources = resources;

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
    /// The skill's complete file list. It must include an entry matching the skill's own
    /// <see cref="SkillEntry.Uri"/>, carrying the digest and size of its <c>SKILL.md</c>.
    /// </param>
    /// <returns>A manifest wrapping a defensive copy of <paramref name="resources"/>.</returns>
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
    public bool IsDynamic => _resources is null;

    /// <summary>
    /// Gets the enumerated files, or <see langword="null"/> when <see cref="IsDynamic"/> is <see langword="true"/>.
    /// </summary>
    public IReadOnlyList<SkillResource>? Resources => _resources;
}
