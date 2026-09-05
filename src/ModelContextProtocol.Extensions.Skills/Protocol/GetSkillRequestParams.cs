using ModelContextProtocol.Protocol;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Represents the parameters for a <c>skills/get</c> request retrieving a single skill's entry by URI.
/// </summary>
/// <remarks>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/pull/2640">SEP-2640</see>
/// specification for details.
/// </remarks>
public sealed class GetSkillRequestParams : RequestParams
{
    /// <summary>
    /// Gets or sets the URI of the skill's <c>SKILL.md</c>.
    /// </summary>
    /// <remarks>
    /// If the URI does not identify a skill the server serves, the server returns error -32602
    /// (Invalid params), the same code <c>resources/read</c> uses for unknown resources.
    /// </remarks>
    [JsonPropertyName("uri")]
    public required string Uri { get; set; }
}
