using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents a known resource template that the server is capable of reading.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public record ResourceTemplate
{
    /// <summary>
    /// The URI template (according to RFC 6570) that can be used to construct resource URIs.
    /// </summary>
    [JsonPropertyName("uriTemplate")]
    public required string UriTemplate { get; init; }

    /// <summary>
    /// A human-readable name for this resource template.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// A description of what this resource template represents.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// The MIME type of this resource template, if known.
    /// </summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; init; }

    /// <summary>
    /// Optional annotations for the resource template.
    /// </summary>
    [JsonPropertyName("annotations")]
    public Annotations? Annotations { get; init; }
}