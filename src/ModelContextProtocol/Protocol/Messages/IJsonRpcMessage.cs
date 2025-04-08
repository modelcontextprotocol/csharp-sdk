using ModelContextProtocol.Utils.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// Base interface for all JSON-RPC messages in the Model Context Protocol (MCP).
/// </summary>
/// <remarks>
/// This interface serves as the foundation for all message types in the JSON-RPC 2.0 protocol
/// used by MCP, including requests, responses, notifications, and errors. JSON-RPC is a stateless,
/// lightweight remote procedure call (RPC) protocol that uses JSON as its data format.
/// 
/// Messages implementing this interface are automatically serialized and deserialized using the
/// <see cref="JsonRpcMessageConverter"/> to handle the polymorphic nature of the different message types.
/// </remarks>
/// <example>
/// The following concrete implementations of this interface are used in the protocol:
/// <code>
/// // A request message (expects a response)
/// var request = new JsonRpcRequest 
/// { 
///     Id = RequestId.Generate(), 
///     Method = "weather.getTemperature",
///     Params = JsonNode.Parse("{\"location\":\"Seattle\"}")
/// };
/// 
/// // A notification message (does not expect a response)
/// var notification = new JsonRpcNotification
/// {
///     Method = "weather.update",
///     Params = JsonNode.Parse("{\"location\":\"Seattle\",\"temperature\":72}")
/// };
/// </code>
/// </example>
[JsonConverter(typeof(JsonRpcMessageConverter))]
public interface IJsonRpcMessage
{
    /// <summary>
    /// JSON-RPC protocol version. Must be "2.0".
    /// </summary>
    string JsonRpc { get; }
}
