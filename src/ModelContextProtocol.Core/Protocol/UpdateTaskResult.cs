using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the result of a <c>tasks/update</c> request. This is an empty acknowledgement.
/// </summary>
/// <remarks>
/// <para>
/// On success, the server acknowledges the request with an empty result.
/// The acknowledgement is eventually consistent: the server may accept the responses and
/// return the ack before the task's observable status reflects them.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md">SEP-2663</see>
/// specification for details.
/// </para>
/// </remarks>
public sealed class UpdateTaskResult : Result
{
}
