using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Messages;
/// <summary>
/// A successful response message in the JSON-RPC protocol.
/// </summary>
/// <remarks>
/// Response messages are sent in reply to a request message and contain the result of the method execution.
/// Each response includes the same ID as the original request, allowing the sender to match responses
/// with their corresponding requests.
/// 
/// This class represents a successful response with a result. For error responses, see <see cref="JsonRpcError"/>.
/// </remarks>
/// <example>
/// Creating and sending a response to a request:
/// <code>
/// // When handling a request in a server implementation:
/// public async Task HandleRequestAsync(JsonRpcRequest request)
/// {
///     try
///     {
///         // Process the request
///         if (request.Method == "weather.getTemperature")
///         {
///             // Extract parameters
///             var location = request.Params?["location"]?.GetValue&lt;string&gt;();
///             
///             // Get the temperature for the requested location
///             var temperature = await GetTemperatureForLocationAsync(location);
///             
///             // Send a successful response with the result
///             var response = new JsonRpcResponse
///             {
///                 Id = request.Id,
///                 Result = JsonValue.Create(temperature)
///             };
///             
///             await SendMessageAsync(response);
///         }
///     }
///     catch (Exception ex)
///     {
///         // Send an error response if something went wrong
///         var error = new JsonRpcError
///         {
///             Id = request.Id,
///             Error = new JsonRpcErrorDetail
///             {
///                 Code = ErrorCodes.InternalError,
///                 Message = ex.Message
///             }
///         };
///         
///         await SendMessageAsync(error);
///     }
/// }
/// </code>
/// </example>
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
    /// <remarks>
    /// <para>
    /// This property contains the result data returned by the server in response to the client request.
    /// For Model Context Protocol operations, this typically contains a serialized response object 
    /// specific to the method that was called, such as <see cref="Types.InitializeResult"/>, 
    /// <see cref="Types.GetPromptResult"/>, or other result types.
    /// </para>
    /// <para>
    /// The client code typically deserializes this JSON node into the appropriate result type
    /// using <c>JsonSerializer.Deserialize</c>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Example of accessing the Result property in a client
    /// var response = await sessionTransport.ReceiveMessageAsync&lt;JsonRpcResponse&gt;(cancellationToken);
    /// 
    /// // Deserialize the Result into a specific type based on the request method
    /// var temperature = response.Result?.GetValue&lt;int&gt;();
    /// Console.WriteLine($"The temperature is {temperature}°C");
    /// 
    /// // Or deserialize to a complex type
    /// var initResult = JsonSerializer.Deserialize&lt;InitializeResult&gt;(response.Result);
    /// </code>
    /// </example>
    /// <seealso cref="Types.InitializeResult"/>
    /// <seealso cref="Types.GetPromptResult"/>
    /// <seealso cref="Types.ListToolsResult"/>
    [JsonPropertyName("result")]
    public required JsonNode? Result { get; init; }
}
