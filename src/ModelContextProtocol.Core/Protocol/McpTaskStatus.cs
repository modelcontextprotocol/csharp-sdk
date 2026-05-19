using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the status of an MCP task.
/// </summary>
/// <remarks>
/// Tasks are durable state machines that carry information about the underlying execution state
/// of the request they augment. See the
/// <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md">SEP-2663</see>
/// specification for details.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<McpTaskStatus>))]
public enum McpTaskStatus
{
    /// <summary>
    /// The request is currently being processed.
    /// </summary>
    [JsonStringEnumMemberName("working")]
    Working,

    /// <summary>
    /// The server needs input from the client before the task can proceed.
    /// The <c>tasks/get</c> response will include outstanding requests in the <c>inputRequests</c> field.
    /// </summary>
    [JsonStringEnumMemberName("input_required")]
    InputRequired,

    /// <summary>
    /// The request completed successfully and results are available.
    /// This includes tool calls that returned results with <c>isError: true</c>.
    /// </summary>
    [JsonStringEnumMemberName("completed")]
    Completed,

    /// <summary>
    /// The request was cancelled before completion.
    /// </summary>
    [JsonStringEnumMemberName("cancelled")]
    Cancelled,

    /// <summary>
    /// The request failed due to a JSON-RPC error during execution.
    /// This status must not be used for non-JSON-RPC errors.
    /// </summary>
    [JsonStringEnumMemberName("failed")]
    Failed,
}
