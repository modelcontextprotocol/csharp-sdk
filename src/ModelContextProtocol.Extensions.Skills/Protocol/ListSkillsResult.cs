using ModelContextProtocol.Protocol;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Represents a server's response to a <c>skills/list</c> request, containing the skills it serves.
/// </summary>
/// <remarks>
/// <para>
/// The result may be empty or partial. A server whose skill catalog is large, generated on demand, or
/// otherwise unenumerable may return fewer skills than it serves, and hosts must not treat an empty listing as
/// proof that a server has no skills. Skills absent from a listing remain retrievable through <c>skills/get</c>.
/// </para>
/// <para>
/// An entry is atomic: a skill's manifest is never split across pages.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/ext-skills/blob/main/specification/stable/skills.mdx">Skills extension specification</see>
/// for details.
/// </para>
/// </remarks>
public sealed class ListSkillsResult : PaginatedResult, ICacheableResult
{
    /// <summary>
    /// Gets or sets the skill entries.
    /// </summary>
    [JsonPropertyName("skills")]
    public IList<Skill> Skills { get; set; } = [];

    /// <inheritdoc />
    [JsonPropertyName("ttlMs")]
    [JsonConverter(typeof(TimeSpanMillisecondsConverter))]
    public TimeSpan? TimeToLive { get; set; }

    /// <inheritdoc />
    [JsonPropertyName("cacheScope")]
    [JsonConverter(typeof(CacheScopeConverter))]
    public CacheScope? CacheScope { get; set; }
}
