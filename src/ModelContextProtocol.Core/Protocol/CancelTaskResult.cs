using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the result of a <c>tasks/cancel</c> request. This is an empty acknowledgement.
/// </summary>
/// <remarks>
/// <para>
/// The server acknowledges the request with an empty result. Cancellation processing is
/// eventually consistent — the task's observable status may remain <see cref="McpTaskStatus.Working"/>
/// after the ack, and may ultimately reach a terminal status other than <see cref="McpTaskStatus.Cancelled"/>
/// if the work finished before cancellation could take effect.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md">SEP-2663</see>
/// specification for details.
/// </para>
/// </remarks>
public sealed class CancelTaskResult : Result
{
}
