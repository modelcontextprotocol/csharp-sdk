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
