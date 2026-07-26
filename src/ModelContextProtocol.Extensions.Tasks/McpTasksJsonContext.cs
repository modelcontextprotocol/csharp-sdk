using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Extensions.Tasks;

/// <summary>
/// Provides source-generated JSON serialization metadata for MCP Tasks extension types.
/// </summary>
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CreateTaskResult))]
[JsonSerializable(typeof(GetTaskRequestParams))]
[JsonSerializable(typeof(GetTaskResult))]
[JsonSerializable(typeof(WorkingTaskResult))]
[JsonSerializable(typeof(CompletedTaskResult))]
[JsonSerializable(typeof(FailedTaskResult))]
[JsonSerializable(typeof(CancelledTaskResult))]
[JsonSerializable(typeof(InputRequiredTaskResult))]
[JsonSerializable(typeof(UpdateTaskRequestParams))]
[JsonSerializable(typeof(UpdateTaskResult))]
[JsonSerializable(typeof(CancelTaskRequestParams))]
[JsonSerializable(typeof(CancelTaskResult))]
[JsonSerializable(typeof(TaskStatusNotificationParams))]
[JsonSerializable(typeof(WorkingTaskNotificationParams))]
[JsonSerializable(typeof(CompletedTaskNotificationParams))]
[JsonSerializable(typeof(FailedTaskNotificationParams))]
[JsonSerializable(typeof(CancelledTaskNotificationParams))]
[JsonSerializable(typeof(InputRequiredTaskNotificationParams))]
public sealed partial class McpTasksJsonContext : JsonSerializerContext
{
}
