using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents annotations that can be attached to content, resources, and resource templates.
/// </summary>
/// <remarks>
/// Annotations enable filtering and prioritization of content for different audiences.
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public record Annotations
{
    /// <summary>
    /// Gets or sets the intended audience for this content as an array of <see cref="Role"/> values.
    /// </summary>
    [JsonPropertyName("audience")]
    public Role[]? Audience { get; init; }

    /// <summary>
    /// Gets or sets a value indicating how important this data is for operating the server.
    /// </summary>
    /// <remarks>
    /// The value is a floating-point number between 0 and 1, where 0 represents the lowest priority
    /// 1 represents highest priority.
    /// </remarks>
    [JsonPropertyName("priority")]
    public float? Priority { get; init; }
}
