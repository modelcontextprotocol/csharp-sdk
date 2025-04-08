using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// A request message in the JSON-RPC protocol.
/// </summary>
/// <remarks>
/// Requests are messages that require a response from the receiver. Each request includes a unique ID
/// that will be included in the corresponding response message (either a success response or an error).
/// 
/// The receiver of a request message is expected to execute the specified method with the provided parameters
/// and return either a <see cref="JsonRpcResponse"/> with the result, or a <see cref="JsonRpcError"/>
/// if the method execution fails.
/// </remarks>
/// <example>
/// Creating and sending a request, then handling the response:
/// <code>
/// // Create a request to get the temperature for a location
/// var request = new JsonRpcRequest
/// {
///     Id = RequestId.Generate(),
///     Method = "weather.getTemperature",
///     Params = JsonNode.Parse(@"{""location"": ""Seattle""}")
/// };
/// 
/// // Send the request and await a response
/// var response = await session.SendRequestAsync&lt;JsonNode&gt;(request);
/// 
/// // Check if we got a success or error response
/// if (response is JsonRpcResponse successResponse)
/// {
///     // Handle the successful result
///     var temperature = successResponse.Result?.GetValue&lt;int&gt;();
///     Console.WriteLine($"Temperature in Seattle: {temperature}°F");
/// }
/// else if (response is JsonRpcError errorResponse)
/// {
///     // Handle the error
///     Console.WriteLine($"Error {errorResponse.Error.Code}: {errorResponse.Error.Message}");
/// }
/// </code>
/// </example>
public record JsonRpcRequest : IJsonRpcMessageWithId
{
    /// <summary>
    /// JSON-RPC protocol version. Always "2.0".
    /// </summary>
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    /// <summary>
    /// Request identifier. Must be a string or number and unique within the session.
    /// </summary>
    [JsonPropertyName("id")]
    public RequestId Id { get; set; }

    /// <summary>
    /// Name of the method to invoke.
    /// </summary>
    [JsonPropertyName("method")]
    public required string Method { get; init; }

    /// <summary>
    /// Optional parameters for the method.
    /// </summary>
    [JsonPropertyName("params")]
    public JsonNode? Params { get; init; }
}
