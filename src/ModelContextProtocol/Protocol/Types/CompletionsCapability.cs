using ModelContextProtocol.Server;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the completions capability configuration.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class CompletionsCapability
{
    // Currently empty in the spec, but may be extended in the future.

    /// <summary>
    /// Gets or sets the handler for get completion requests.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<CompleteRequestParams>, CancellationToken, Task<CompleteResult>>? CompleteHandler { get; set; }
}