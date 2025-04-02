using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// A notification message in the JSON-RPC protocol (a request that doesn't expect a response).
/// </summary>
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
