using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the type of role in the Model Context Protocol conversation.
/// </summary>
[JsonConverter(typeof(CustomizableJsonStringEnumConverter<Role>))]
public enum Role
{
    /// <summary>
    /// Corresponds to a human user in the conversation.
    /// </summary>
    [JsonStringEnumMemberName("user")]
    User,

    /// <summary>
    /// Corresponds to the AI assistant in the conversation.
    /// </summary>
    [JsonStringEnumMemberName("assistant")]
    Assistant
}