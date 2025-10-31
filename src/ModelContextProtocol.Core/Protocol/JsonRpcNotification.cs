using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a notification message in the JSON-RPC protocol.
/// </summary>
/// <remarks>
/// Notifications are messages that do not require a response and are not matched with a response message.
/// They are useful for one-way communication, such as log notifications and progress updates.
/// Unlike requests, notifications do not include an ID field, since there will be no response to match with it.
/// </remarks>
public sealed class JsonRpcNotification : JsonRpcMessage
{
    /// <summary>
    /// Gets or sets the name of the notification method.
    /// </summary>
    [JsonPropertyName("method")]
    public required string Method { get; set; }

    /// <summary>
    /// Gets or sets optional parameters for the notification.
    /// </summary>
    [JsonPropertyName("params")]
    public JsonNode? Params { get; set; }
}
