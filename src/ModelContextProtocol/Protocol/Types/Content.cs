using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the content of a tool response.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// There are multiple subtypes of content, depending on the "type" field, these are represented as separate classes.
/// </summary>
public class Content
{
    /// <summary>
    /// The type of content. This determines the structure of the content object. Can be "image", "audio", "text", "resource".
    /// </summary>

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// The text content of the message.
    /// </summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>
    /// The base64-encoded image data.
    /// </summary>
    [JsonPropertyName("data")]
    public string? Data { get; set; }

    /// <summary>
    /// The MIME type of the image.
    /// </summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>
    /// The resource content of the message (if embedded).
    /// </summary>
    [JsonPropertyName("resource")]
    public ResourceContents? Resource { get; set; }

    /// <summary>
    /// Optional annotations for the content.
    /// </summary>
    [JsonPropertyName("annotations")]
    public Annotations? Annotations { get; init; }
}