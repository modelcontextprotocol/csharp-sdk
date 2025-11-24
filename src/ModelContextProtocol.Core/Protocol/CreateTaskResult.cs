using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the response to a task-augmented request.
/// </summary>
/// <remarks>
/// <para>
/// When a client sends a request with a <c>task</c> parameter, the server immediately returns
/// a <see cref="CreateTaskResult"/> containing the created task information instead of the
/// normal result type. The actual result can be retrieved later via <c>tasks/result</c>.
/// </para>
/// <para>
/// This type is returned for any task-augmented request including <c>tools/call</c>,
/// <c>sampling/createMessage</c>, and <c>elicitation/create</c>.
/// </para>
/// </remarks>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class CreateTaskResult : Result
{
    /// <summary>
    /// Gets or sets the task data for the newly created task.
    /// </summary>
    [JsonPropertyName("task")]
    public McpTask Task { get; set; } = null!;
}
