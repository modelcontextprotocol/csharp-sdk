using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Messages;
/// <summary>
/// A successful response message in the JSON-RPC protocol.
/// </summary>
public record JsonRpcResponse : IJsonRpcMessageWithId
{
    /// <summary>
    /// JSON-RPC protocol version. Always "2.0".
    /// </summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    /// <summary>
    /// Request identifier matching the original request.
    /// </summary>
    [JsonPropertyName("id")]
    public required RequestId Id { get; init; }

    /// <summary>
    /// The result of the method invocation.
    /// </summary>
    [JsonPropertyName("result")]
    public required JsonNode? Result { get; init; }
}
