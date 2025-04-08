using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Base class for all request parameters.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/#L771-L806">See the schema for details</see>
/// </summary>
public abstract class RequestParams
{
    /// <summary>
    /// Metadata related to the request that provides additional protocol-level information.
    /// This can include progress tracking tokens and other protocol-specific properties
    /// that are not part of the primary request parameters.
    /// </summary>
    /// <example>
    /// <code>
    /// var requestParams = new CallToolRequestParams
    /// {
    ///     Name = "myTool",
    ///     Meta = new RequestParamsMetadata
    ///     {
    ///         ProgressToken = new ProgressToken("abc123"),
    ///     }
    /// };
    /// </code>
    /// </example>
    [JsonPropertyName("_meta")]
    public RequestParamsMetadata? Meta { get; init; }
}
