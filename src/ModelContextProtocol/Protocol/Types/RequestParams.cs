using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Provides a base class for all request parameters.
/// </summary>
/// <remarks>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public abstract class RequestParams
{
    /// <summary>
    /// Gets or sets metadata related to the request that provides additional protocol-level information.
    /// </summary>
    /// <remarks>
    /// This can include progress tracking tokens and other protocol-specific properties
    /// that are not part of the primary request parameters.
    /// </remarks>
    [JsonPropertyName("_meta")]
    public RequestParamsMetadata? Meta { get; init; }
}
