using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents annotations that can be attached to content, resources, and resource templates.
/// </summary>
/// <remarks>
/// Annotations enable filtering and prioritization of content for different audiences.
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public sealed class Annotations
{
    /// <summary>
    /// Gets or sets the intended audience for this content as an array of <see cref="Role"/> values.
    /// </summary>
    [JsonPropertyName("audience")]
    public IList<Role>? Audience { get; set; }

    /// <summary>
    /// Gets or sets a value indicating how important this data is for operating the server.
    /// </summary>
    /// <remarks>
    /// The value is a floating-point number between 0 and 1, where 0 represents the lowest priority
    /// and 1 represents the highest priority.
    /// </remarks>
    [JsonPropertyName("priority")]
    public float? Priority { get; set; }

    /// <summary>
    /// Gets or sets the moment the resource was last modified.
    /// </summary>
    /// <remarks>
    /// The corresponding JSON should be an ISO 8601 formatted string (for example, \"2025-01-12T15:00:58Z\").
    /// Examples of when the resource was last modified include last activity in an open file or when the resource was attached.
    /// </remarks>
    [JsonPropertyName("lastModified")]
    public DateTimeOffset? LastModified { get; set; }
}
