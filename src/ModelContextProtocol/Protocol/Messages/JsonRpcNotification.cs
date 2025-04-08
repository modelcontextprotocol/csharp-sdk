using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// A notification message in the JSON-RPC protocol (a request that doesn't expect a response).
/// </summary>
/// <remarks>
/// Notifications are messages that do not require a response and are not matched with a response message.
/// They are useful for one-way communication, such as event notifications, progress updates, or other
/// informational messages where no acknowledgment is needed.
/// 
/// Unlike requests, notifications do not include an ID field, since there will be no response to match with it.
/// </remarks>
/// <example>
/// Creating and sending a notification:
/// <code>
/// // Create a notification about a weather update
/// var notification = new JsonRpcNotification
/// {
///     Method = "weather.update",
///     Params = JsonNode.Parse(@"{
///         ""location"": ""Seattle"",
///         ""temperature"": 72,
///         ""condition"": ""Cloudy""
///     }")
/// };
/// 
/// // Send the notification
/// await endpoint.SendMessageAsync(notification);
/// </code>
/// </example>
public record JsonRpcNotification : IJsonRpcMessage
{
    /// <summary>
    /// JSON-RPC protocol version. Always "2.0".
    /// </summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    /// <summary>
    /// Name of the notification method.
    /// </summary>
    [JsonPropertyName("method")]
    public required string Method { get; init; }

    /// <summary>
    /// Optional parameters for the notification.
    /// </summary>
    [JsonPropertyName("params")]
    public JsonNode? Params { get; init; }
}
