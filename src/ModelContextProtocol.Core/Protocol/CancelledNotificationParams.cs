using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a notification indicating that a request has been cancelled by the client,
/// and that any associated processing should cease immediately.
/// </summary>
/// <remarks>
/// This class is typically used in conjunction with the <see cref="NotificationMethods.CancelledNotification"/>
/// method identifier. When a client sends this notification, the server should attempt to
/// cancel any ongoing operations associated with the specified request ID.
/// </remarks>
public sealed class CancelledNotificationParams : NotificationParams
{
    /// <summary>
    /// Gets or sets the ID of the request to cancel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When provided, this must correspond to the ID of a request previously issued in the same direction.
    /// </para>
    /// <para>
    /// This property is required (non-null) when cancelling non-task requests.
    /// For task cancellation, use the <c>tasks/cancel</c> request instead, and do not set this property.
    /// </para>
    /// </remarks>
    [JsonPropertyName("requestId")]
    public RequestId? RequestId { get; set; }

    /// <summary>
    /// Gets or sets an optional string describing the reason for the cancellation request.
    /// </summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}
