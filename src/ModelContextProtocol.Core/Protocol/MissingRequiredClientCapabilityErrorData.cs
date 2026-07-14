using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the payload for the <see cref="McpErrorCode.MissingRequiredClientCapability"/> JSON-RPC error.
/// </summary>
/// <remarks>
/// Introduced by the 2026-07-28 protocol revision (SEP-2575). When a server cannot fulfill a request because
/// the client did not declare a required capability in its per-request
/// <c>_meta/io.modelcontextprotocol/clientCapabilities</c> field, it MUST return this error so clients
/// know which capabilities to advertise on a retry.
/// </remarks>
public sealed class MissingRequiredClientCapabilityErrorData
{
    /// <summary>
    /// Gets or sets the client capabilities the server requires to process the request.
    /// </summary>
    [JsonPropertyName("requiredCapabilities")]
    public required ClientCapabilities RequiredCapabilities { get; set; }
}
