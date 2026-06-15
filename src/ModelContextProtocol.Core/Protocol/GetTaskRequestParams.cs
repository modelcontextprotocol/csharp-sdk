using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters for a <c>tasks/get</c> request to poll for task completion.
/// </summary>
/// <remarks>
/// <para>
/// Clients poll for task completion by sending <c>tasks/get</c> requests.
/// Clients should respect the <see cref="GetTaskResult.PollIntervalMs"/> provided in responses
/// when determining polling frequency.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md">SEP-2663</see>
/// specification for details.
/// </para>
/// </remarks>
public sealed class GetTaskRequestParams : RequestParams
{
    /// <summary>
    /// Gets or sets the identifier of the task to query.
    /// </summary>
    [JsonPropertyName("taskId")]
    public required string TaskId { get; set; }
}
