using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the content of a resource.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
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
}