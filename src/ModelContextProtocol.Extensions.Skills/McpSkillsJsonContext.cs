using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Provides source-generated JSON serialization metadata for the MCP Skills extension types.
/// </summary>
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Skill))]
[JsonSerializable(typeof(SkillResource))]
[JsonSerializable(typeof(SkillResources))]
[JsonSerializable(typeof(ListSkillsRequestParams))]
[JsonSerializable(typeof(ListSkillsResult))]
[JsonSerializable(typeof(GetSkillRequestParams))]
[JsonSerializable(typeof(GetSkillResult))]
public sealed partial class McpSkillsJsonContext : JsonSerializerContext
{
}
