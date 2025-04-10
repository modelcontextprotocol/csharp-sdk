using ModelContextProtocol.Utils.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// Represents any JSON-RPC message used in the Model Context Protocol (MCP).
/// </summary>
/// <remarks>
/// This interface serves as the foundation for all message types in the JSON-RPC 2.0 protocol
/// used by MCP, including requests, responses, notifications, and errors. JSON-RPC is a stateless,
/// lightweight remote procedure call (RPC) protocol that uses JSON as its data format.
/// </remarks>
[JsonConverter(typeof(JsonRpcMessageConverter))]
public interface IJsonRpcMessage
{
    /// <summary>
    /// Gets the JSON-RPC protocol version used.
    /// </summary>
    string JsonRpc { get; }
}
