using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// Represents an error response message in the JSON-RPC protocol.
/// </summary>
/// <remarks>
/// <para>
/// Error responses are sent when a request cannot be fulfilled or encounters an error during processing.
/// Like successful responses, error messages include the same ID as the original request, allowing the
/// sender to match errors with their corresponding requests.
/// </para>
/// <para>
/// Each error response contains a structured error detail object with a numeric code, descriptive message,
/// and optional additional data to provide more context about the error.
/// </para>
/// </remarks>
public record JsonRpcError : IJsonRpcMessageWithId
{
    /// <inheritdoc />
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    /// <inheritdoc />
    [JsonPropertyName("id")]
    public required RequestId Id { get; init; }

    /// <summary>
    /// Gets detailed error information for the failed request, containing an error code, 
    /// message, and optional additional data
    /// </summary>
    [JsonPropertyName("error")]
    public required JsonRpcErrorDetail Error { get; init; }
}
