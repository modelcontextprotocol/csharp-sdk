using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the type of role in the conversation.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<Role>))]
public enum Role
{
    /// <summary>
    /// Corresponds to the user in the conversation.
    /// </summary>
    [JsonStringEnumMemberName("user")]
    User,

    /// <summary>
    /// Corresponds to the AI in the conversation.
    /// </summary>
    [JsonStringEnumMemberName("assistant")]
    Assistant
}