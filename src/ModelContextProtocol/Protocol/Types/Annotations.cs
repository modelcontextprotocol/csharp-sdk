using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents annotations that can be attached to content, resources, and resource templates.
/// Annotations enable filtering and prioritization of content for different audiences.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
/// <example>
/// <code>
/// // Create content with annotations for both User and Assistant with high priority
/// var errorContent = new Content
/// {
///     Type = "text",
///     Text = "Error: Operation failed",
///     Annotations = new() { Audience = [Role.User, Role.Assistant], Priority = 1.0f }
/// };
/// 
/// // Create content with annotations for User only with medium priority
/// var userContent = new Content
/// {
///     Type = "text",
///     Text = "Operation completed successfully",
///     Annotations = new() { Audience = [Role.User], Priority = 0.7f }
/// };
/// 
/// // Create content with annotations for Assistant only with low priority
/// var assistantContent = new Content
/// {
///     Type = "text",
///     Text = "Debug: Cache hit ratio 0.95, latency 150ms",
///     Annotations = new() { Audience = [Role.Assistant], Priority = 0.3f }
/// };
/// </code>
/// </example>
public record Annotations
{
    /// <summary>
    /// Specifies the intended audience for this content as an array of Role values.
    /// This property enables filtering and routing content to specific roles in the system.
    /// </summary>
    /// <remarks>
    /// Common patterns include:
    /// <list type="bullet">
    ///   <item><description>[Role.User, Role.Assistant] - Content intended for both user and assistant</description></item>
    ///   <item><description>[Role.User] - Content intended only for the user</description></item>
    ///   <item><description>[Role.Assistant] - Content intended only for the assistant</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    ///     Annotations = new() { Audience = [Role.User, Role.Assistant], Priority = 1.0f }
    ///     Annotations = new() { Audience = [Role.User], Priority = 0.7f }
    ///     Annotations = new() { Audience = [Role.Assistant], Priority = 0.3f }
    /// </code>
    /// </example>
    [JsonPropertyName("audience")]
    public Role[]? Audience { get; init; }

    /// <summary>
    /// Describes how important this data is for operating the server on a scale from 0 to 1,
    /// where 0 represents lowest priority and 1 represents highest priority.
    /// </summary>
    /// <remarks>
    /// Must be a value between 0 and 1. Higher values indicate content that should be given
    /// more prominence or attention by the receiving role(s).
    /// </remarks>
    /// <example>
    /// <code>
    ///     Annotations = new() { Audience = [Role.User, Role.Assistant], Priority = 1.0f } // Highest priority
    ///     Annotations = new() { Audience = [Role.User], Priority = 0.7f }                 // Medium-high priority
    ///     Annotations = new() { Audience = [Role.Assistant], Priority = 0.3f }            // Low priority
    /// </code>
    /// </example>
    [JsonPropertyName("priority")]
    public float? Priority { get; init; }
}
