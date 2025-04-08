using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// Represents a notification indicating that a request has been cancelled by the client,
/// and that any associated processing SHOULD cease immediately.
/// </summary>
/// <remarks>
/// This class is used in conjunction with the <see cref="NotificationMethods.CancelledNotification"/>
/// method identifier. When a client sends this notification, the server should attempt to
/// cancel any ongoing operations associated with the specified request ID.
/// </remarks>
/// <example>
/// <code>
/// // Client code to cancel an in-progress request
/// await client.SendNotificationAsync(
///     NotificationMethods.CancelledNotification,
///     parameters: new CancelledNotification
///     {
///         RequestId = requestId,
///         Reason = "Operation no longer needed"
///     },
///     cancellationToken);
/// </code>
/// </example>
public sealed class CancelledNotification
{
    /// <summary>
    /// The ID of the request to cancel. This must match the ID of an in-flight request
    /// that the sender wishes to cancel.
    /// </summary>
    [JsonPropertyName("requestId")]
    public RequestId RequestId { get; set; }

    /// <summary>
    /// An optional string describing the reason for the cancellation.
    /// </summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}