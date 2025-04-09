using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// Represents a notification message in the JSON-RPC protocol.
/// </summary>
/// <remarks>
/// Notifications are messages that do not require a response and are not matched with a response message.
/// They are useful for one-way communication, such as log notifications and progress updates.
/// Unlike requests, notifications do not include an ID field, since there will be no response to match with it.
/// </remarks>
public record JsonRpcNotification : IJsonRpcMessage
{
    /// <inheritdoc />
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    /// <summary>
    /// Gets the name of the notification method.
    /// </summary>
    [JsonPropertyName("method")]
    public required string Method { get; init; }

    /// <summary>
    /// Gets optional parameters for the notification.
    /// </summary>
    [JsonPropertyName("params")]
    public JsonNode? Params { get; init; }
}
