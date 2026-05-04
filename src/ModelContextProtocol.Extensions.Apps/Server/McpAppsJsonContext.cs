using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Server;

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
internal sealed partial class McpAppsJsonContext : JsonSerializerContext
{
}
