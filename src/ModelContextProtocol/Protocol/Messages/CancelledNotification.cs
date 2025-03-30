using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// This notification indicates that the result will be unused, so any associated processing SHOULD cease.
/// </summary>
public sealed class CancelledNotification
{
    /// <summary>
    /// The ID of the request to cancel.
    /// </summary>
    [JsonPropertyName("requestId")]
    public RequestId RequestId { get; set; }

    /// <summary>
    /// An optional string describing the reason for the cancellation.
    /// </summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}