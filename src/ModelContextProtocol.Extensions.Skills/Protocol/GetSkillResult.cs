using ModelContextProtocol.Protocol;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Represents a server's response to a <c>skills/get</c> request, containing one skill's entry.
/// </summary>
/// <remarks>
/// <para>
/// A server answers for every skill it serves, whether or not that skill appears in its <c>skills/list</c>
/// result. The result carries no pagination cursor.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/ext-skills/blob/main/specification/stable/skills.mdx">Skills extension specification</see>
/// for details.
/// </para>
/// </remarks>
public sealed class GetSkillResult : Result
{
    /// <summary>
    /// Gets or sets the skill's entry, identical in shape and meaning to an entry of <c>skills/list</c>.
    /// </summary>
    [JsonPropertyName("skill")]
    public required Skill Skill { get; set; }
}
