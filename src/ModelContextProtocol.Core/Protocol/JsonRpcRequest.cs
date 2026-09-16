using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a request message in the JSON-RPC protocol.
/// </summary>
/// <remarks>
/// Requests are messages that require a response from the receiver. Each request includes a unique ID
/// that will be included in the corresponding response message (either a success response or an error).
///
/// The receiver of a request message is expected to execute the specified method with the provided parameters
/// and return either a <see cref="JsonRpcResponse"/> with the result, or a <see cref="JsonRpcError"/>
/// if the method execution fails.
/// </remarks>
public sealed class JsonRpcRequest : JsonRpcMessageWithId
{
    /// <summary>
    /// Gets or sets the name of the method to invoke.
    /// </summary>
    [JsonPropertyName("method")]
    public required string Method { get; set; }

    /// <summary>
    /// Gets or sets optional parameters for the method.
    /// </summary>
    [JsonPropertyName("params")]
    public JsonNode? Params { get; set; }

    internal JsonRpcRequest WithId(RequestId id)
    {
        return new JsonRpcRequest
        {
            JsonRpc = JsonRpc,
            Id = id,
            Method = Method,
            Params = Params,
            Context = Context,
        };
    }
}
