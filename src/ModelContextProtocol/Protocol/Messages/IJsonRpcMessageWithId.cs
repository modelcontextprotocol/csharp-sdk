namespace ModelContextProtocol.Protocol.Messages;

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
public interface IJsonRpcMessageWithId : IJsonRpcMessage
{
    /// <summary>
    /// Gets the message identifier.
    /// </summary>
    /// <remarks>
    /// Each ID is expected to be unique within the context of a given session.
    /// </remarks>
    RequestId Id { get; }
}
