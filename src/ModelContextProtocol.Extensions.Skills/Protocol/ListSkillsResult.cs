using ModelContextProtocol.Protocol;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Represents a server's response to a <c>skills/list</c> request, containing the skills it serves.
/// </summary>
/// <remarks>
/// <para>
/// The result may be empty or partial. A server whose skill catalog is large, generated on demand, or
/// otherwise unenumerable may return fewer skills than it serves, and hosts must not treat an empty
/// listing as proof that a server has no skills. Skills absent from a listing remain retrievable through
/// <c>skills/get</c>.
/// </para>
/// <para>
/// An entry is atomic: a skill's manifest is never split across pages.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/pull/2640">SEP-2640</see>
/// specification for details.
/// </para>
/// </remarks>
public sealed class ListSkillsResult : PaginatedResult, ICacheableResult
{
    /// <summary>
    /// Gets or sets the skill entries.
    /// </summary>
    [JsonPropertyName("skills")]
    public IList<SkillEntry> Skills { get; set; } = [];

    /// <inheritdoc />
    [JsonPropertyName("ttlMs")]
    [JsonConverter(typeof(TimeSpanMillisecondsConverter))]
    public TimeSpan? TimeToLive { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// Core applies its own internal <c>CacheScopeConverter</c> to this property on the built-in result
    /// types, which tolerates unrecognized scope strings on read by mapping them to
    /// <see langword="null"/>. That converter is not accessible outside the core assembly, so this type
    /// relies on the converter declared on the <see cref="Protocol.CacheScope"/> enum instead. The written
    /// wire values are identical; only the read-side leniency differs.
    /// </remarks>
    [JsonPropertyName("cacheScope")]
    public CacheScope? CacheScope { get; set; }
}
