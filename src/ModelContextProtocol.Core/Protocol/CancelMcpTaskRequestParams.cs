using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters for a tasks/cancel request to explicitly cancel a task.
/// </summary>
/// <remarks>
/// <para>
/// Receivers must reject cancellation requests for tasks already in a terminal status
/// (<see cref="McpTaskStatus.Completed"/>, <see cref="McpTaskStatus.Failed"/>, or
/// <see cref="McpTaskStatus.Cancelled"/>) with error code -32602 (Invalid params).
/// </para>
/// <para>
/// Upon receiving a valid cancellation request, receivers should attempt to stop the task
/// execution and must transition the task to <see cref="McpTaskStatus.Cancelled"/> status
/// before sending the response.
/// </para>
/// </remarks>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class CancelMcpTaskRequestParams : RequestParams
{
    /// <summary>
    /// Gets or sets the unique identifier of the task to cancel.
    /// </summary>
    [JsonPropertyName("taskId")]
    public required string TaskId { get; set; }
}

/// <summary>
/// Represents the result of a tasks/cancel request.
/// </summary>
/// <remarks>
/// The result contains the updated task state after cancellation. The task will be in
/// <see cref="McpTaskStatus.Cancelled"/> status if the cancellation was successful.
/// </remarks>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class CancelMcpTaskResult : Result
{
    /// <summary>
    /// Gets or sets the task ID.
    /// </summary>
    [JsonPropertyName("taskId")]
    public required string TaskId { get; set; }

    /// <summary>
    /// Gets or sets the current status of the task (should be <see cref="McpTaskStatus.Cancelled"/>).
    /// </summary>
    [JsonPropertyName("status")]
    public required McpTaskStatus Status { get; set; }

    /// <summary>
    /// Gets or sets an optional message describing the cancellation.
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
