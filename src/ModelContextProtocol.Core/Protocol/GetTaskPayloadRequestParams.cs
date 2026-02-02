using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters for a tasks/result request to retrieve the result of a completed task.
/// </summary>
/// <remarks>
/// <para>
/// This request blocks until the task reaches a terminal status (<see cref="McpTaskStatus.Completed"/>,
/// <see cref="McpTaskStatus.Failed"/>, or <see cref="McpTaskStatus.Cancelled"/>).
/// </para>
/// <para>
/// The result structure matches the original request type (e.g., <see cref="CallToolResult"/> for tools/call).
/// This is distinct from the initial <see cref="Result"/> response, which contains only task data.
/// </para>
/// </remarks>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class GetTaskPayloadRequestParams : RequestParams
{
    /// <summary>
    /// Gets or sets the unique identifier of the task whose result to retrieve.
    /// </summary>
    [JsonPropertyName("taskId")]
    public required string TaskId { get; set; }
}
