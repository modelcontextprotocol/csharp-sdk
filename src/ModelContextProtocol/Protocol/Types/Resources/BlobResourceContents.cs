using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Binary contents of a resource.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/2024-11-05/schema.json">See the schema for details</see>
/// </summary>
public class BlobResourceContents : ResourceContents
{
    /// <summary>
    /// The binary content of the resource.
    /// </summary>
    [JsonPropertyName("blob")]
    public string Blob { get; set; } = default!;
}