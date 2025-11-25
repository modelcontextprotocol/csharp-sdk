using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters used with a <see cref="NotificationMethods.ElicitationCompleteNotification"/>
/// notification emitted after a URL-mode elicitation finishes out-of-band.
/// </summary>
/// <remarks>
/// <para>
/// The payload references the original elicitation by ID so that clients can resume deferred
/// requests or update pending UI once the external flow completes.
/// </para>
/// </remarks>
public sealed class ElicitationCompleteNotificationParams : NotificationParams
{
    /// <summary>
    /// Gets or sets the unique identifier of the elicitation that completed.
    /// </summary>
    /// <remarks>
    /// This matches <see cref="ElicitRequestParams.ElicitationId"/> from the originating request and allows
    /// clients to correlate the completion notification with previously issued prompts.
    /// </remarks>
    [JsonPropertyName("elicitationId")]
    public required string ElicitationId { get; set; }
}
