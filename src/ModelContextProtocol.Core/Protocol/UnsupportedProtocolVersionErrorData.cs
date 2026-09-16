using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the payload for the <see cref="McpErrorCode.UnsupportedProtocolVersion"/> JSON-RPC error.
/// </summary>
/// <remarks>
/// Introduced by the 2026-07-28 protocol revision (SEP-2575). When a server receives a request whose
/// declared protocol version it does not implement, it MUST return this error so clients can
/// fall back to a mutually supported version.
/// </remarks>
public sealed class UnsupportedProtocolVersionErrorData
{
    /// <summary>
    /// Gets or sets the protocol version strings that the server supports.
    /// </summary>
    [JsonPropertyName("supported")]
    public required IList<string> Supported { get; set; }

    /// <summary>
    /// Gets or sets the protocol version requested by the client.
    /// </summary>
    [JsonPropertyName("requested")]
    public required string Requested { get; set; }
}
