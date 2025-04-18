using ModelContextProtocol.Protocol.Transport;
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
public abstract class JsonRpcMessage
{
    /// <summary>
    /// Gets the JSON-RPC protocol version used.
    /// </summary>
    /// <inheritdoc />
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    /// <summary>
    /// Gets or sets the transport the <see cref="JsonRpcMessage"/> was received on or should be sent over.
    /// </summary>
    /// <remarks>
    /// This is used to support the Streamable HTTP transport where the specification states that the server
    /// SHOULD include JSON-RPC responses in the HTTP response body for the POST request containing
    /// the corresponding JSON-RPC request. It may be <see langword="null"/> for other transports.
    /// </remarks>
    [JsonIgnore]
    public ITransport? RelatedTransport { get; set; }
}
