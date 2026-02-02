using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters for a notifications/tasks/status notification.
/// </summary>
/// <remarks>
/// <para>
/// When a task status changes, receivers may send this notification to inform the
/// requestor of the change. This notification includes the full task state.
/// </para>
/// <para>
/// Requestors must not rely on receiving this notification, as it is optional. Receivers
/// are not required to send status notifications and may choose to only send them for
/// certain status transitions. Requestors should continue to poll via tasks/get to ensure
/// they receive status updates.
/// </para>
/// </remarks>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class McpTaskStatusNotificationParams : NotificationParams
{
    /// <summary>
    /// Gets or sets the task ID.
    /// </summary>
    [JsonPropertyName("taskId")]
    public required string TaskId { get; set; }

    /// <summary>
    /// Gets or sets the current status of the task.
    /// </summary>
    [JsonPropertyName("status")]
    public required McpTaskStatus Status { get; set; }

    /// <summary>
    /// Gets or sets an optional human-readable message describing the current state.
    /// </summary>
    [JsonPropertyName("statusMessage")]
    public string? StatusMessage { get; set; }

    /// <summary>
    /// Gets or sets the ISO 8601 timestamp when the task was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the ISO 8601 timestamp when the task status was last updated.
    /// </summary>
    [JsonPropertyName("lastUpdatedAt")]
    public required DateTimeOffset LastUpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the time to live (retention duration) from creation before the task may be deleted.
    /// </summary>
    [JsonPropertyName("ttl")]
    [JsonConverter(typeof(TimeSpanMillisecondsConverter))]
    public TimeSpan? TimeToLive { get; set; }

    /// <summary>
    /// Gets or sets the suggested time between status checks.
    /// </summary>
    [JsonPropertyName("pollInterval")]
    [JsonConverter(typeof(TimeSpanMillisecondsConverter))]
    public TimeSpan? PollInterval { get; set; }
}
