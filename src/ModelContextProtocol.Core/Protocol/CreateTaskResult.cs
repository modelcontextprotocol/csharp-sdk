using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the result returned by a server when it creates a task in lieu of a standard result.
/// </summary>
/// <remarks>
/// <para>
/// A server returns <see cref="CreateTaskResult"/> instead of the standard result shape (e.g., <see cref="CallToolResult"/>)
/// to indicate that the request will be processed asynchronously. The client then uses
/// <see cref="TaskId"/> for subsequent <c>tasks/get</c>, <c>tasks/update</c>, and <c>tasks/cancel</c> calls.
/// </para>
/// <para>
/// A server must not return <see cref="CreateTaskResult"/> to a client that did not include the
/// <c>io.modelcontextprotocol/tasks</c> extension capability on its request.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md">SEP-2663</see>
/// specification for details.
/// </para>
/// </remarks>
public sealed class CreateTaskResult : Result
{
    /// <summary>
    /// Gets or sets the stable identifier for this task.
    /// </summary>
    [JsonPropertyName("taskId")]
    public required string TaskId { get; set; }

    /// <summary>
    /// Gets or sets the current task status.
    /// </summary>
    [JsonPropertyName("status")]
    public required McpTaskStatus Status { get; set; }

    /// <summary>
    /// Gets or sets an optional message describing the current task state.
    /// </summary>
    [JsonPropertyName("statusMessage")]
    public string? StatusMessage { get; set; }

    /// <summary>
    /// Gets or sets the ISO 8601 timestamp when the task was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the ISO 8601 timestamp when the task was last updated.
    /// </summary>
    [JsonPropertyName("lastUpdatedAt")]
    public required DateTimeOffset LastUpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the time-to-live duration from creation, or <see langword="null"/> for unlimited.
    /// </summary>
    [JsonPropertyName("ttlMs")]
    [JsonConverter(typeof(TimeSpanMillisecondsConverter))]
    public TimeSpan? TimeToLive { get; set; }

    /// <summary>
    /// Gets or sets the suggested polling interval in milliseconds.
    /// </summary>
    [JsonPropertyName("pollIntervalMs")]
    public long? PollIntervalMs { get; set; }
}
