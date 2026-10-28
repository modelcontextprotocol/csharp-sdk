using ModelContextProtocol.Protocol;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Represents the parameters for a <c>skills/get</c> request retrieving a single skill's entry by URI.
/// </summary>
/// <remarks>
/// See the <see href="https://github.com/modelcontextprotocol/ext-skills/blob/main/specification/stable/skills.mdx">Skills extension specification</see>
/// for details.
/// </remarks>
public sealed class GetSkillRequestParams : RequestParams
{
    /// <summary>
    /// Gets or sets the URI of the skill's <c>SKILL.md</c>.
    /// </summary>
    /// <remarks>
    /// If the URI does not identify a skill the server serves, the server returns error <c>-32602</c>
    /// (Invalid params), the same code <c>resources/read</c> uses for unknown resources.
    /// </remarks>
    [JsonPropertyName("uri")]
    public required string Uri { get; set; }
}
