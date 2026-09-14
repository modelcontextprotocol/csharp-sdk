using ModelContextProtocol.Protocol;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Represents a server's response to a <c>skills/get</c> request, containing one skill's entry.
/// </summary>
/// <remarks>
/// <para>
/// A server answers for every skill it serves, whether or not that skill appears in its <c>skills/list</c>
/// result. The result carries no pagination cursor, because a single entry is not a list.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/pull/2640">SEP-2640</see>
/// specification for details.
/// </para>
/// </remarks>
public sealed class GetSkillResult : Result
{
    /// <summary>
    /// Gets or sets the skill's entry, identical in shape and meaning to an entry of <c>skills/list</c>.
    /// </summary>
    [JsonPropertyName("skill")]
    public required SkillEntry Skill { get; set; }
}
