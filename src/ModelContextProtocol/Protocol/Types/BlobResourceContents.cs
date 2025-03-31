using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Binary contents of a resource.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class BlobResourceContents : ResourceContents
{
    /// <summary>
    /// The base64-encoded string representing the binary data of the item.
    /// </summary>
    [JsonPropertyName("blob")]
    public string Blob { get; set; } = string.Empty;
}