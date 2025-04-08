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
    /// 
    /// This can be used by clients to populate UI elements.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// A description of what this resource represents.
    /// 
    /// This can be used by clients to improve the LLM's understanding of available resources. It can be thought of like a \"hint\" to the model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The description should provide clear context about the resource's content, format, and purpose.
    /// This helps AI models make better decisions about when to access or reference the resource.
    /// </para>
    /// <para>
    /// Client applications can also use this description for display purposes in user interfaces
    /// or to help users understand the available resources.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var resource = new Resource
    /// {
    ///     Uri = "file://documents/report.pdf",
    ///     Name = "Quarterly Report",
    ///     Description = "Q3 2023 financial report with company performance metrics and analysis",
    ///     MimeType = "application/pdf"
    /// };
    /// </code>
    /// </example>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// The MIME type of this resource, if known.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Specifies the format of the resource content, helping clients to properly interpret and display the data.
    /// Common MIME types include "text/plain" for plain text, "application/pdf" for PDF documents,
    /// "image/png" for PNG images, "application/json" for JSON data, etc.
    /// </para>
    /// <para>
    /// This property may be null if the MIME type is unknown or not applicable for the resource.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var resource = new Resource
    /// {
    ///     Uri = "file://documents/report.pdf",
    ///     Name = "Quarterly Report",
    ///     Description = "Q3 2023 financial report with company performance metrics and analysis",
    ///     MimeType = "application/pdf"
    /// };
    /// </code>
    /// </example>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; init; }

    /// <summary>
    /// The size of the raw resource content, in bytes (i.e., before base64 encoding or any tokenization), if known.
    /// 
    /// This can be used by Hosts to display file sizes and estimate context window usage.
    /// </summary>
    [JsonPropertyName("size")]
    public long? Size { get; init; }

    /// <summary>
    /// Optional annotations for the resource.
    /// These annotations can be used to specify the intended audience (User, Assistant, or both)
    /// and the priority level of the resource. Clients can use this information to filter
    /// or prioritize resources for different roles.
    /// </summary>
    [JsonPropertyName("annotations")]
    public Annotations? Annotations { get; init; }
}
