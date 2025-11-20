using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Provides metadata related to the request that provides additional protocol-level information.
/// </summary>
/// <remarks>
/// This class contains properties that are used by the Model Context Protocol
/// for features like progress tracking and other protocol-specific capabilities.
/// </remarks>
public sealed class RequestParamsMetadata
{
    /// <summary>
    /// Gets or sets an opaque token that will be attached to any subsequent progress notifications.
    /// </summary>
    /// <remarks>
    /// The receiver is not obligated to provide these notifications.
    /// </remarks>
    [JsonPropertyName("progressToken")]
    public ProgressToken? ProgressToken { get; set; }
}
