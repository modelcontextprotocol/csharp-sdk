using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters for a tasks/get request to retrieve task status.
/// </summary>
/// <remarks>
/// Requestors poll for task completion by sending tasks/get requests. They should
/// respect the <see cref="McpTask.PollInterval"/> provided in responses when determining
/// polling frequency.
/// </remarks>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class GetTaskRequestParams : RequestParams
{
    /// <summary>
    /// Gets or sets the unique identifier of the task to retrieve.
    /// </summary>
    [JsonPropertyName("taskId")]
    public required string TaskId { get; set; }
}

/// <summary>
/// Represents the result of a tasks/get request.
/// </summary>
/// <remarks>
/// The result contains the current state of the task, including its status, timestamps,
/// and any status message.
/// </remarks>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class GetTaskResult : Result
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
