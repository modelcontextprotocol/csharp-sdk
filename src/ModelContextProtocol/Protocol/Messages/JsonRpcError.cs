using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// An error response message in the JSON-RPC protocol.
/// </summary>
/// <remarks>
/// Error responses are sent when a request cannot be fulfilled or encounters an error during processing.
/// Like successful responses, error messages include the same ID as the original request, allowing the
/// sender to match errors with their corresponding requests.
/// 
/// Each error response contains a structured error detail object with a numeric code, descriptive message,
/// and optional additional data to provide more context about the error.
/// </remarks>
/// <example>
/// Creating and sending an error response (in a JSON-RPC endpoint implementation):
/// <code>
/// // When handling a request that encounters an error:
/// public async Task HandleRequestAsync(JsonRpcRequest request)
/// {
///     try
///     {
///         // Method implementation that might throw an exception
///         if (request.Method == "weather.getTemperature")
///         {
///             var location = request.Params?["location"]?.GetValue<string>();
///             
///             if (string.IsNullOrEmpty(location))
///             {
///                 // Send an invalid params error
///                 var invalidParamsError = new JsonRpcError
///                 {
///                     Id = request.Id,
///                     Error = new JsonRpcErrorDetail
///                     {
///                         Code = ErrorCodes.InvalidParams,
///                         Message = "Missing required 'location' parameter",
///                         Data = JsonNode.Parse(@"{""requiredParams"": [""location""]}")
///                     }
///                 };
///                 
///                 await SendMessageAsync(invalidParamsError);
///                 return;
///             }
///             
///             // Continue with normal processing...
///         }
///         else
///         {
///             // Method not found error
///             var methodNotFoundError = new JsonRpcError
///             {
///                 Id = request.Id,
///                 Error = new JsonRpcErrorDetail
///                 {
///                     Code = ErrorCodes.MethodNotFound,
///                     Message = $"Method '{request.Method}' not found"
///                 }
///             };
///             
///             await SendMessageAsync(methodNotFoundError);
///         }
///     }
///     catch (Exception ex)
///     {
///         // Internal error for unhandled exceptions
///         var internalError = new JsonRpcError
///         {
///             Id = request.Id,
///             Error = new JsonRpcErrorDetail
///             {
///                 Code = ErrorCodes.InternalError,
///                 Message = "An internal error occurred while processing the request",
///                 Data = JsonNode.Parse($"{{\"errorDetails\": \"{ex.Message}\"}}")
///             }
///         };
///         
///         await SendMessageAsync(internalError);
///     }
/// }
///
/// // Handling a received error response:
/// public async Task ProcessResponseAsync(IJsonRpcMessageWithId response)
/// {
///     if (response is JsonRpcError errorResponse)
///     {
///         // Handle error based on error code
///         switch (errorResponse.Error.Code)
///         {
///             case ErrorCodes.InvalidParams:
///                 Console.WriteLine($"Invalid parameters: {errorResponse.Error.Message}");
///                 break;
///             case ErrorCodes.MethodNotFound:
///                 Console.WriteLine($"Method not found: {errorResponse.Error.Message}");
///                 break;
///             default:
///                 Console.WriteLine($"Error {errorResponse.Error.Code}: {errorResponse.Error.Message}");
///                 break;
///         }
///     }
///     else if (response is JsonRpcResponse successResponse)
///     {
///         // Process successful response
///     }
/// }
/// </code>
/// </example>
public record JsonRpcError : IJsonRpcMessageWithId
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
    /// Detailed error information for the failed request, containing an error code, 
    /// message, and optional additional data. This property provides structured
    /// information about the error that occurred during processing of the JSON-RPC request.
    /// </summary>
    /// <example>
    /// Example error handling:
    /// <code>
    /// if (response is JsonRpcError errorResponse)
    /// {
    ///     Console.WriteLine($"Error code: {errorResponse.Error.Code}");
    ///     Console.WriteLine($"Error message: {errorResponse.Error.Message}");
    /// }
    /// </code>
    /// </example>
    [JsonPropertyName("error")]
    public required JsonRpcErrorDetail Error { get; init; }
}
