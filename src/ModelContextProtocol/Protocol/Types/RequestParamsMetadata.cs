using ModelContextProtocol.Protocol.Messages;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Metadata related to the request.
/// </summary>
public class RequestParamsMetadata
{
    /// <summary>
    /// If specified, the caller is requesting out-of-band progress notifications for this request (as represented by notifications/progress). The value of this parameter is an opaque token that will be attached to any subsequent notifications. The receiver is not obligated to provide these notifications.
    /// </summary>
    [JsonPropertyName("progressToken")]
    public ProgressToken? ProgressToken { get; set; } = default!;
}
