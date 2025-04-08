namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// Base interface for JSON-RPC messages that include an ID.
/// </summary>
/// <remarks>
/// In the JSON-RPC protocol, messages with an ID require a response from the receiver.
/// This includes request messages (which expect a matching response) and response messages
/// (which include the ID of the original request they're responding to).
/// 
/// The ID is used to correlate requests with their responses, allowing asynchronous
/// communication where multiple requests can be sent without waiting for responses.
/// </remarks>
/// <example>
/// Message types that implement this interface:
/// <code>
/// // A request message with an ID (expects a response)
/// var request = new JsonRpcRequest 
/// { 
///     Id = RequestId.Generate(), 
///     Method = "weather.getTemperature"
/// };
/// 
/// // A successful response message with the same ID
/// var response = new JsonRpcResponse
/// {
///     Id = request.Id,
///     Result = JsonNode.Parse("72")
/// };
/// 
/// // An error response message with the same ID
/// var error = new JsonRpcError
/// {
///     Id = request.Id,
///     Error = new JsonRpcErrorDetail
///     {
///         Code = ErrorCodes.InternalError,
///         Message = "Unable to retrieve temperature data"
///     }
/// };
/// </code>
/// </example>
public interface IJsonRpcMessageWithId : IJsonRpcMessage
{
    /// <summary>
    /// The message identifier.
    /// </summary>
    RequestId Id { get; }
}
