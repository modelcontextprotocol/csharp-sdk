using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters for a <c>tasks/update</c> request to provide input responses
/// to outstanding server-to-client requests on a task.
/// </summary>
/// <remarks>
/// <para>
/// When a task requires input from the client (indicated by <see cref="McpTaskStatus.InputRequired"/>),
/// the server includes outstanding requests in the <c>inputRequests</c> field of the <c>tasks/get</c> response.
/// The client provides responses via the inherited <see cref="RequestParams.InputResponses"/> field in
/// <c>tasks/update</c> requests; the wire format matches the typed envelope defined by the Multi Round-Trip
/// Requests (MRTR) extension (SEP-2322).
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md">SEP-2663</see>
/// specification for details.
/// </para>
/// </remarks>
public sealed class UpdateTaskRequestParams : RequestParams
{
    /// <summary>
    /// Gets or sets the identifier of the task to update.
    /// </summary>
    [JsonPropertyName("taskId")]
    public required string TaskId { get; set; }
}
