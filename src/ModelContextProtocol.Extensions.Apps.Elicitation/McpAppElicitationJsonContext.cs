using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Apps.Elicitation;

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(McpAppElicitationCapability))]
[JsonSerializable(typeof(McpAppElicitationMeta))]
internal sealed partial class McpAppElicitationJsonContext : JsonSerializerContext
{
}
