using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents a known resource that the server is capable of reading.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public record Resource
{
    /// <summary>
    /// The URI of this resource.
    /// </summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>
    /// A human-readable name for this resource.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// A description of what this resource represents.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// The MIME type of this resource, if known.
    /// </summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; init; }

    /// <summary>
    /// Optional annotations for the resource.
    /// </summary>
    [JsonPropertyName("annotations")]
    public Annotations? Annotations { get; init; }
}
