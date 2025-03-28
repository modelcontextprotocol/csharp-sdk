using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// The client's response to a sampling/create_message request from the server. 
/// The client should inform the user before returning the sampled message, to allow them to inspect the response (human in the loop) 
///  and decide whether to allow the server to see it.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class CreateMessageResult
{
    /// <summary>
    /// Text or image content of the message.
    /// </summary>
    [JsonPropertyName("content")]
    public required Content Content { get; init; }

    /// <summary>
    /// The name of the model that generated the message.
    /// </summary>
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>
    /// The reason why sampling stopped, if known.
    /// </summary>
    [JsonPropertyName("stopReason")]
    public string? StopReason { get; init; }

    /// <summary>
    /// The role of the user who generated the message.
    /// </summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }
}
