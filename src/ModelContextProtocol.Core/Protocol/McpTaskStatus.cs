using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the status of an MCP task.
/// </summary>
/// <remarks>
/// <para>
/// Tasks progress through a defined lifecycle:
/// <list type="bullet">
/// <item><description><see cref="Working"/>: The request is currently being processed.</description></item>
/// <item><description><see cref="InputRequired"/>: The receiver needs input from the requestor. 
/// The requestor should call tasks/result to receive input requests.</description></item>
/// <item><description><see cref="Completed"/>: The request completed successfully and results are available.</description></item>
/// <item><description><see cref="Failed"/>: The request did not complete successfully.</description></item>
/// <item><description><see cref="Cancelled"/>: The request was cancelled before completion.</description></item>
/// </list>
/// </para>
/// <para>
/// Terminal states are <see cref="Completed"/>, <see cref="Failed"/>, and <see cref="Cancelled"/>.
/// Once a task reaches a terminal state, it cannot transition to any other status.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<McpTaskStatus>))]
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public enum McpTaskStatus
{
    /// <summary>
    /// The request is currently being processed.
    /// </summary>
    /// <remarks>
    /// Tasks begin in this status when created. From <see cref="Working"/>, tasks may transition
    /// to <see cref="InputRequired"/>, <see cref="Completed"/>, <see cref="Failed"/>, or <see cref="Cancelled"/>.
    /// </remarks>
    [JsonStringEnumMemberName("working")]
    Working,

    /// <summary>
    /// The receiver needs input from the requestor.
    /// </summary>
    /// <remarks>
    /// The requestor should call tasks/result to receive input requests, even though the task
    /// has not reached a terminal state. From <see cref="InputRequired"/>, tasks may transition
    /// to <see cref="Working"/>, <see cref="Completed"/>, <see cref="Failed"/>, or <see cref="Cancelled"/>.
    /// </remarks>
    [JsonStringEnumMemberName("input_required")]
    InputRequired,

    /// <summary>
    /// The request completed successfully and results are available.
    /// </summary>
    /// <remarks>
    /// This is a terminal status. Tasks in this status cannot transition to any other status.
    /// </remarks>
    [JsonStringEnumMemberName("completed")]
    Completed,

    /// <summary>
    /// The associated request did not complete successfully.
    /// </summary>
    /// <remarks>
    /// This is a terminal status. For tool calls specifically, this includes cases where
    /// the tool call result has isError set to true. Tasks in this status cannot transition
    /// to any other status.
    /// </remarks>
    [JsonStringEnumMemberName("failed")]
    Failed,

    /// <summary>
    /// The request was cancelled before completion.
    /// </summary>
    /// <remarks>
    /// This is a terminal status. Tasks in this status cannot transition to any other status.
    /// </remarks>
    [JsonStringEnumMemberName("cancelled")]
    Cancelled
}
