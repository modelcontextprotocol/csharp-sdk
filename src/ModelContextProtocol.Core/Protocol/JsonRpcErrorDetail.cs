using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents detailed error information for JSON-RPC error responses.
/// </summary>
/// <remarks>
/// This class is used as part of the <see cref="JsonRpcError"/> message to provide structured
/// error information when a request cannot be fulfilled. The JSON-RPC 2.0 specification defines
/// a standard format for error responses that includes a numeric code, a human-readable message,
/// and optional additional data.
/// </remarks>
public sealed class JsonRpcErrorDetail
{
    /// <summary>
    /// Gets or sets an integer error code according to the JSON-RPC specification.
    /// </summary>
    [JsonPropertyName("code")]
    public required int Code { get; set; }

    /// <summary>
    /// Gets or sets a short description of the error.
    /// </summary>
    /// <remarks>
    /// This description is expected to be a brief, human-readable explanation of what went wrong.
    /// For standard error codes, it's recommended to use the descriptions defined
    /// in the JSON-RPC 2.0 specification.
    /// </remarks>
    [JsonPropertyName("message")]
    public required string Message { get; set; }

    /// <summary>
    /// Gets or sets optional additional error data.
    /// </summary>
    /// <remarks>
    /// This property can contain any additional information that might help the client
    /// understand or resolve the error. Common examples include validation errors,
    /// stack traces (in development environments), or contextual information about
    /// the error condition.
    /// </remarks>
    [JsonPropertyName("data")]
    public object? Data { get; set; }
}
