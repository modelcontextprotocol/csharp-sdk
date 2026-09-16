using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a successful response message in the JSON-RPC protocol.
/// </summary>
/// <remarks>
/// <para>
/// Response messages are sent in reply to a request message and contain the result of the method execution.
/// Each response includes the same ID as the original request, allowing the sender to match responses
/// with their corresponding requests.
/// </para>
/// <para>
/// This class represents a successful response with a result. For error responses, see <see cref="JsonRpcError"/>.
/// </para>
/// </remarks>
public sealed class JsonRpcResponse : JsonRpcMessageWithId
{
    /// <summary>
    /// Gets or sets the result of the method invocation.
    /// </summary>
    /// <remarks>
    /// This property contains the result data returned by the server in response to the JSON-RPC method request.
    /// </remarks>
    [JsonPropertyName("result")]
    public required JsonNode? Result { get; set; }
}
