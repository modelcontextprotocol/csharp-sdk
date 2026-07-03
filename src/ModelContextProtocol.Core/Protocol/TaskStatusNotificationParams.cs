using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters for a <c>notifications/tasks</c> notification sent by the server
/// to push task status updates to the client.
/// </summary>
/// <remarks>
/// <para>
/// Each notification carries a complete task state for the current status, identical to what
/// <c>tasks/get</c> would have returned at that moment. The concrete type depends on the task's
/// current status:
/// <list type="bullet">
///   <item><see cref="WorkingTaskNotificationParams"/> — <see cref="McpTaskStatus.Working"/></item>
///   <item><see cref="CompletedTaskNotificationParams"/> — <see cref="McpTaskStatus.Completed"/></item>
///   <item><see cref="FailedTaskNotificationParams"/> — <see cref="McpTaskStatus.Failed"/></item>
///   <item><see cref="CancelledTaskNotificationParams"/> — <see cref="McpTaskStatus.Cancelled"/></item>
///   <item><see cref="InputRequiredTaskNotificationParams"/> — <see cref="McpTaskStatus.InputRequired"/></item>
/// </list>
/// </para>
/// <para>
/// To receive task status notifications, clients send a <c>subscriptions/listen</c> request
/// including the task IDs they are interested in.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md">SEP-2663</see>
/// specification for details.
/// </para>
/// </remarks>
[JsonConverter(typeof(Converter))]
public abstract class TaskStatusNotificationParams : NotificationParams
{
    /// <summary>Prevent external derivations.</summary>
    private protected TaskStatusNotificationParams()
    {
    }

    /// <summary>
    /// Gets or sets the stable identifier for this task.
    /// </summary>
    [JsonPropertyName("taskId")]
    public required string TaskId { get; set; }

    /// <summary>
    /// Gets or sets the current task status.
    /// </summary>
    [JsonPropertyName("status")]
    public abstract McpTaskStatus Status { get; }

    /// <summary>
    /// Gets or sets an optional message describing the current task state.
    /// </summary>
    [JsonPropertyName("statusMessage")]
    public string? StatusMessage { get; set; }

    /// <summary>
    /// Gets or sets the ISO 8601 timestamp when the task was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the ISO 8601 timestamp when the task was last updated.
    /// </summary>
    [JsonPropertyName("lastUpdatedAt")]
    public required DateTimeOffset LastUpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the time-to-live duration from creation, or <see langword="null"/> for unlimited.
    /// </summary>
    [JsonPropertyName("ttlMs")]
    public TimeSpan? TimeToLive { get; set; }

    /// <summary>
    /// Gets or sets the suggested polling interval in milliseconds.
    /// </summary>
    [JsonPropertyName("pollIntervalMs")]
    public long? PollIntervalMs { get; set; }

    /// <summary>
    /// JSON converter that deserializes <see cref="TaskStatusNotificationParams"/> to the appropriate
    /// concrete subtype based on the <c>status</c> discriminator field.
    /// </summary>
    internal sealed class Converter : JsonConverter<TaskStatusNotificationParams>
    {
        public override TaskStatusNotificationParams? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected StartObject token for TaskStatusNotificationParams.");
            }

            string? taskId = null;
            string? statusString = null;
            string? statusMessage = null;
            DateTimeOffset? createdAt = null;
            DateTimeOffset? lastUpdatedAt = null;
            long? ttlMs = null;
            long? pollIntervalMs = null;
            JsonObject? meta = null;
            JsonElement? result = null;
            JsonElement? error = null;
            Dictionary<string, InputRequest>? inputRequests = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected property name.");
                }

                string propertyName = reader.GetString()!;
                reader.Read();

                switch (propertyName)
                {
                    case "taskId":
                        taskId = reader.GetString();
                        break;
                    case "status":
                        statusString = reader.GetString();
                        break;
                    case "statusMessage":
                        statusMessage = reader.GetString();
                        break;
                    case "createdAt":
                        createdAt = reader.GetDateTimeOffset();
                        break;
                    case "lastUpdatedAt":
                        lastUpdatedAt = reader.GetDateTimeOffset();
                        break;
                    case "ttlMs":
                        ttlMs = reader.GetInt64();
                        break;
                    case "pollIntervalMs":
                        pollIntervalMs = reader.GetInt64();
                        break;
                    case "_meta":
                        meta = JsonSerializer.Deserialize(ref reader, options.GetTypeInfo<JsonObject>());
                        break;
                    case "result":
                        result = JsonElement.ParseValue(ref reader);
                        break;
                    case "error":
                        error = JsonElement.ParseValue(ref reader);
                        break;
                    case "inputRequests":
                        if (reader.TokenType != JsonTokenType.StartObject)
                        {
                            throw new JsonException("'inputRequests' must be a JSON object.");
                        }
                        inputRequests = new Dictionary<string, InputRequest>(StringComparer.Ordinal);
                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonTokenType.EndObject)
                            {
                                break;
                            }
                            if (reader.TokenType != JsonTokenType.PropertyName)
                            {
                                throw new JsonException("Expected property name in 'inputRequests'.");
                            }
                            string requestKey = reader.GetString()!;
                            reader.Read();
                            var inputRequest = JsonSerializer.Deserialize(ref reader, McpJsonUtilities.JsonContext.Default.InputRequest)
                                ?? throw new JsonException($"Failed to deserialize InputRequest for key '{requestKey}'.");
                            inputRequests[requestKey] = inputRequest;
                        }
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (taskId is null)
            {
                throw new JsonException("Missing required 'taskId' property on TaskStatusNotificationParams.");
            }

            if (statusString is null)
            {
                throw new JsonException("Missing required 'status' property on TaskStatusNotificationParams.");
            }

            if (createdAt is null)
            {
                throw new JsonException("Missing required 'createdAt' property on TaskStatusNotificationParams.");
            }

            if (lastUpdatedAt is null)
            {
                throw new JsonException("Missing required 'lastUpdatedAt' property on TaskStatusNotificationParams.");
            }

            TaskStatusNotificationParams notification = statusString switch
            {
                "working" => new WorkingTaskNotificationParams
                {
                    TaskId = taskId,
                    CreatedAt = createdAt.Value,
                    LastUpdatedAt = lastUpdatedAt.Value,
                },
                "completed" => result is not null
                    ? new CompletedTaskNotificationParams
                    {
                        TaskId = taskId,
                        CreatedAt = createdAt.Value,
                        LastUpdatedAt = lastUpdatedAt.Value,
                        Result = result.Value,
                    }
                    : throw new JsonException("Completed task notification is missing required 'result' property."),
                "failed" => error is not null
                    ? new FailedTaskNotificationParams
                    {
                        TaskId = taskId,
                        CreatedAt = createdAt.Value,
                        LastUpdatedAt = lastUpdatedAt.Value,
                        Error = error.Value,
                    }
                    : throw new JsonException("Failed task notification is missing required 'error' property."),
                "cancelled" => new CancelledTaskNotificationParams
                {
                    TaskId = taskId,
                    CreatedAt = createdAt.Value,
                    LastUpdatedAt = lastUpdatedAt.Value,
                },
                "input_required" => inputRequests is not null
                    ? new InputRequiredTaskNotificationParams
                    {
                        TaskId = taskId,
                        CreatedAt = createdAt.Value,
                        LastUpdatedAt = lastUpdatedAt.Value,
                        InputRequests = inputRequests,
                    }
                    : throw new JsonException("Input-required task notification is missing required 'inputRequests' property."),
                _ => throw new JsonException($"Unknown task status: '{statusString}'.")
            };

            notification.StatusMessage = statusMessage;
            notification.TimeToLive = ttlMs is null ? null : TimeSpan.FromMilliseconds(ttlMs.Value);
            notification.PollIntervalMs = pollIntervalMs;
            notification.Meta = meta;

            return notification;
        }

        public override void Write(Utf8JsonWriter writer, TaskStatusNotificationParams value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            if (value.Meta is not null)
            {
                writer.WritePropertyName("_meta");
                JsonSerializer.Serialize(writer, value.Meta, options.GetTypeInfo<JsonObject>());
            }

            writer.WriteString("taskId", value.TaskId);
            writer.WriteString("status", value.Status switch
            {
                McpTaskStatus.Working => "working",
                McpTaskStatus.Completed => "completed",
                McpTaskStatus.Failed => "failed",
                McpTaskStatus.Cancelled => "cancelled",
                McpTaskStatus.InputRequired => "input_required",
                _ => throw new JsonException($"Unknown McpTaskStatus: {value.Status}")
            });

            if (value.StatusMessage is not null)
            {
                writer.WriteString("statusMessage", value.StatusMessage);
            }

            writer.WriteString("createdAt", value.CreatedAt);
            writer.WriteString("lastUpdatedAt", value.LastUpdatedAt);

            if (value.TimeToLive is not null)
            {
                writer.WriteNumber("ttlMs", (long)value.TimeToLive.Value.TotalMilliseconds);
            }

            if (value.PollIntervalMs is not null)
            {
                writer.WriteNumber("pollIntervalMs", value.PollIntervalMs.Value);
            }

            switch (value)
            {
                case CompletedTaskNotificationParams completed:
                    writer.WritePropertyName("result");
                    completed.Result.WriteTo(writer);
                    break;
                case FailedTaskNotificationParams failed:
                    writer.WritePropertyName("error");
                    failed.Error.WriteTo(writer);
                    break;
                case InputRequiredTaskNotificationParams inputRequired:
                    writer.WritePropertyName("inputRequests");
                    writer.WriteStartObject();
                    if (inputRequired.InputRequests is { } reqs)
                    {
                        foreach (var kvp in reqs)
                        {
                            writer.WritePropertyName(kvp.Key);
                            JsonSerializer.Serialize(writer, kvp.Value, McpJsonUtilities.JsonContext.Default.InputRequest);
                        }
                    }
                    writer.WriteEndObject();
                    break;
            }

            writer.WriteEndObject();
        }
    }
}

/// <summary>
/// Task notification for a task that is currently being processed.
/// </summary>
public sealed class WorkingTaskNotificationParams : TaskStatusNotificationParams
{
    /// <inheritdoc/>
    public override McpTaskStatus Status => McpTaskStatus.Working;
}

/// <summary>
/// Task notification for a task that has completed successfully.
/// </summary>
public sealed class CompletedTaskNotificationParams : TaskStatusNotificationParams
{
    /// <inheritdoc/>
    public override McpTaskStatus Status => McpTaskStatus.Completed;

    /// <summary>
    /// Gets or sets the final result of the task.
    /// </summary>
    [JsonPropertyName("result")]
    public required JsonElement Result { get; set; }
}

/// <summary>
/// Task notification for a task that failed.
/// </summary>
public sealed class FailedTaskNotificationParams : TaskStatusNotificationParams
{
    /// <inheritdoc/>
    public override McpTaskStatus Status => McpTaskStatus.Failed;

    /// <summary>
    /// Gets or sets the JSON-RPC error that caused the task to fail.
    /// </summary>
    [JsonPropertyName("error")]
    public required JsonElement Error { get; set; }
}

/// <summary>
/// Task notification for a task that was cancelled.
/// </summary>
public sealed class CancelledTaskNotificationParams : TaskStatusNotificationParams
{
    /// <inheritdoc/>
    public override McpTaskStatus Status => McpTaskStatus.Cancelled;
}

/// <summary>
/// Task notification for a task that requires input from the client.
/// </summary>
public sealed class InputRequiredTaskNotificationParams : TaskStatusNotificationParams
{
    /// <inheritdoc/>
    public override McpTaskStatus Status => McpTaskStatus.InputRequired;

    /// <summary>
    /// Gets or sets the server-to-client requests that need to be fulfilled.
    /// </summary>
    /// <remarks>
    /// Keys are arbitrary identifiers for matching requests to responses. Each value is an
    /// <see cref="InputRequest"/> wrapping the server-to-client request payload, matching
    /// the typed format defined by the Multi Round-Trip Requests (MRTR) extension (SEP-2322).
    /// </remarks>
    [JsonPropertyName("inputRequests")]
    public IDictionary<string, InputRequest>? InputRequests { get; set; }
}

