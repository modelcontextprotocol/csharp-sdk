using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Describes the name and version of an MCP implementation.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class Implementation
{
    /// <summary>
    /// Name of the implementation.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Version of the implementation.
    /// </summary>
    [JsonPropertyName("version")]
    public required string Version { get; set; }
}