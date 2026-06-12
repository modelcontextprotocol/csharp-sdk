using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters for a <c>tasks/cancel</c> request to signal intent to cancel an in-progress task.
/// </summary>
/// <remarks>
/// <para>
/// Cancellation is cooperative: the request signals intent, and the server decides whether and when to honor it.
/// A server is not obligated to actually stop the work; it is only obligated to acknowledge the request.
/// Eventual transition to <see cref="McpTaskStatus.Cancelled"/> is not guaranteed.
/// </para>
/// <para>
/// The <c>notifications/cancelled</c> notification must not be used for task cancellation.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md">SEP-2663</see>
/// specification for details.
/// </para>
/// </remarks>
public sealed class CancelTaskRequestParams : RequestParams
{
    /// <summary>
    /// Gets or sets the identifier of the task to cancel.
    /// </summary>
    [JsonPropertyName("taskId")]
    public required string TaskId { get; set; }
}
