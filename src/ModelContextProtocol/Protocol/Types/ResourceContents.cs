using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// A known resource that the server is capable of reading.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/2024-11-05/schema.json">See the schema for details</see>
/// </summary>
public class ResourceContents
{
    /// <summary>
    /// The URI of the resource.
    /// </summary>
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    /// <summary>
    /// The type of content.
    /// </summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>
    /// The text content of the resource.
    /// </summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }


    /// <summary>
    /// The base64-encoded binary content of the resource.
    /// </summary>
    [JsonPropertyName("blob")]
    public string? Blob { get; set; }

    /// <summary>
    /// A human-readable name for this resource.\n\nThis can be used by clients to populate UI elements.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// The size of the raw resource content, in bytes (i.e., before base64 encoding or any tokenization), if known.
    /// 
    /// This can be used by Hosts to display file sizes and estimate context window usage.
    /// </summary>
    [JsonPropertyName("size")]
    public long? Size { get; set; }
}