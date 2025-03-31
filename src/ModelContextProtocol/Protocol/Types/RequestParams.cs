using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Base class for all request parameters.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/#L771-L806">See the schema for details</see>
/// </summary>
public abstract class RequestParams
{
    /// <summary>
    /// Metadata related to the tool invocation.
    /// </summary>
    [JsonPropertyName("_meta")]
    public RequestParamsMetadata? Meta { get; init; }
}
