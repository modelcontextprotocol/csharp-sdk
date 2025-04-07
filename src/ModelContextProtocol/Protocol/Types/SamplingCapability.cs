using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Server;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the sampling capability configuration.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class SamplingCapability
{
    // Currently empty in the spec, but may be extended in the future

    /// <summary>Gets or sets the handler for sampling requests.</summary>
    [JsonIgnore]
    public Func<CreateMessageRequestParams?, IProgress<ProgressNotificationValue>, CancellationToken, Task<CreateMessageResult>>? SamplingHandler { get; set; }
}