using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// Detailed error information for JSON-RPC error responses.
/// </summary>
/// <remarks>
/// This record is used as part of the <see cref="JsonRpcError"/> message to provide structured 
/// error information when a request cannot be fulfilled. The JSON-RPC 2.0 specification defines
/// a standard format for error responses that includes a numeric code, a human-readable message,
/// and optional additional data.
/// 
/// The error codes follow a specific convention:
/// - Pre-defined errors in the range -32700 to -32603 are reserved for transport and protocol errors
/// - Server-defined errors should use the range -32000 to -32099
/// - Custom application errors can use any other integer values
/// </remarks>
/// <example>
/// Creating and using a JsonRpcErrorDetail:
/// <code>
/// // Create an error detail for a method not found error
/// var errorDetail = new JsonRpcErrorDetail
/// {
///     Code = ErrorCodes.MethodNotFound,
///     Message = "The requested method 'getWeather' was not found",
///     Data = JsonNode.Parse(@"{""availableMethods"": [""getTemperature"", ""getForecast""]}")
/// };
/// 
/// // Use the error detail in a JSON-RPC error response
/// var errorResponse = new JsonRpcError
/// {
///     Id = request.Id,
///     Error = errorDetail
/// };
/// 
/// // Send the error response
/// await endpoint.SendMessageAsync(errorResponse);
/// </code>
/// </example>
public record JsonRpcErrorDetail
{
    /// <summary>
    /// Integer error code according to the JSON-RPC specification.
    /// Predefined error codes in the range -32700 to -32603 are reserved for transport errors,
    /// as defined in the <see cref="ErrorCodes"/> class. Server implementations may define
    /// additional error codes for specific error conditions.
    /// </summary>
    [JsonPropertyName("code")]
    public required int Code { get; init; }

    /// <summary>
    /// Short description of the error.
    /// </summary>
    /// <remarks>
    /// This should be a brief, human-readable explanation of what went wrong.
    /// For standard error codes, it's recommended to use the descriptions defined 
    /// in the JSON-RPC 2.0 specification.
    /// </remarks>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>
    /// Optional additional error data.
    /// </summary>
    /// <remarks>
    /// This property can contain any additional information that might help the client
    /// understand or resolve the error. Common examples include validation errors,
    /// stack traces (in development environments), or contextual information about
    /// the error condition.
    /// </remarks>
    [JsonPropertyName("data")]
    public object? Data { get; init; }
}