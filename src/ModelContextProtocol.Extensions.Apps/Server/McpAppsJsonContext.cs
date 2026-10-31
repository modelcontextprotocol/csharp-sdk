using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Apps;

/// <summary>
/// Provides source-generated JSON serialization metadata for MCP Apps extension types.
/// </summary>
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(McpUiToolMeta))]
[JsonSerializable(typeof(McpUiClientCapabilities))]
[JsonSerializable(typeof(McpUiResourceMeta))]
[JsonSerializable(typeof(McpUiResourceCsp))]
[JsonSerializable(typeof(McpUiResourcePermissions))]
[JsonSerializable(typeof(McpUiElicitationCapability))]
[JsonSerializable(typeof(McpUiServerCapabilities))]
[JsonSerializable(typeof(McpAppElicitationMeta))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(IList<string>))]
internal sealed partial class McpAppsJsonContext : JsonSerializerContext
{
}
