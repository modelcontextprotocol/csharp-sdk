using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a JSON-RPC message used in the Model Context Protocol (MCP) and that includes an ID.
/// </summary>
/// <remarks>
/// In the JSON-RPC protocol, messages with an ID require a response from the receiver.
/// This includes request messages (which expect a matching response) and response messages
/// (which include the ID of the original request they're responding to).
/// The ID is used to correlate requests with their responses, allowing asynchronous
/// communication where multiple requests can be sent without waiting for responses.
/// </remarks>
public abstract class JsonRpcMessageWithId : JsonRpcMessage
{
    /// <summary>Prevent external derivations.</summary>
    private protected JsonRpcMessageWithId()
    {
    }

    /// <summary>
    /// Gets or sets the message identifier.
    /// </summary>
    /// <remarks>
    /// Each ID is expected to be unique within the context of a given session.
    /// </remarks>
    [JsonPropertyName("id")]
    public RequestId Id { get; set; }
}
