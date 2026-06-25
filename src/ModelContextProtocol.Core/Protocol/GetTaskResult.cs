using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the result of a <c>tasks/get</c> request, containing the full task state.
/// </summary>
/// <remarks>
/// <para>
/// This is the abstract base for status-specific task results. The concrete type returned depends on the
/// task's current <see cref="Status"/>:
/// <list type="bullet">
///   <item><see cref="WorkingTaskResult"/> — <see cref="McpTaskStatus.Working"/></item>
///   <item><see cref="CompletedTaskResult"/> — <see cref="McpTaskStatus.Completed"/></item>
///   <item><see cref="FailedTaskResult"/> — <see cref="McpTaskStatus.Failed"/></item>
///   <item><see cref="CancelledTaskResult"/> — <see cref="McpTaskStatus.Cancelled"/></item>
///   <item><see cref="InputRequiredTaskResult"/> — <see cref="McpTaskStatus.InputRequired"/></item>
/// </list>
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md">SEP-2663</see>
/// specification for details.
/// </para>
/// </remarks>
[JsonConverter(typeof(Converter))]
public abstract class GetTaskResult : Result
{
    /// <summary>Prevent external derivations.</summary>
    private protected GetTaskResult()
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
    /// JSON converter that deserializes <see cref="GetTaskResult"/> to the appropriate concrete subtype
    /// based on the <c>status</c> discriminator field.
    /// </summary>
    internal sealed class Converter : JsonConverter<GetTaskResult>
    {
        public override GetTaskResult? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected StartObject token for GetTaskResult.");
            }

            string? taskId = null;
            string? statusString = null;
            string? statusMessage = null;
            DateTimeOffset? createdAt = null;
            DateTimeOffset? lastUpdatedAt = null;
            long? ttlMs = null;
            long? pollIntervalMs = null;
            string? resultType = null;
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
                    case "resultType":
                        resultType = reader.GetString();
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
                throw new JsonException("Missing required 'taskId' property on GetTaskResult.");
            }

            if (statusString is null)
            {
                throw new JsonException("Missing required 'status' property on GetTaskResult.");
            }

            if (createdAt is null)
            {
                throw new JsonException("Missing required 'createdAt' property on GetTaskResult.");
            }

            if (lastUpdatedAt is null)
            {
                throw new JsonException("Missing required 'lastUpdatedAt' property on GetTaskResult.");
            }

            GetTaskResult taskResult = statusString switch
            {
                "working" => new WorkingTaskResult
                {
                    TaskId = taskId,
                    CreatedAt = createdAt.Value,
                    LastUpdatedAt = lastUpdatedAt.Value,
                },
                "completed" => result is not null
                    ? new CompletedTaskResult
                    {
                        TaskId = taskId,
                        CreatedAt = createdAt.Value,
                        LastUpdatedAt = lastUpdatedAt.Value,
                        Result = result.Value,
                    }
                    : throw new JsonException("Completed task is missing required 'result' property."),
                "failed" => error is not null
                    ? new FailedTaskResult
                    {
                        TaskId = taskId,
                        CreatedAt = createdAt.Value,
                        LastUpdatedAt = lastUpdatedAt.Value,
                        Error = error.Value,
                    }
                    : throw new JsonException("Failed task is missing required 'error' property."),
                "cancelled" => new CancelledTaskResult
                {
                    TaskId = taskId,
                    CreatedAt = createdAt.Value,
                    LastUpdatedAt = lastUpdatedAt.Value,
                },
                "input_required" => inputRequests is not null
                    ? new InputRequiredTaskResult
                    {
                        TaskId = taskId,
                        CreatedAt = createdAt.Value,
                        LastUpdatedAt = lastUpdatedAt.Value,
                        InputRequests = inputRequests,
                    }
                    : throw new JsonException("Input-required task is missing required 'inputRequests' property."),
                _ => throw new JsonException($"Unknown task status: '{statusString}'.")
            };

            taskResult.StatusMessage = statusMessage;
            taskResult.TimeToLive = ttlMs is null ? null : TimeSpan.FromMilliseconds(ttlMs.Value);
            taskResult.PollIntervalMs = pollIntervalMs;
            taskResult.ResultType = resultType;
            taskResult.Meta = meta;

            return taskResult;
        }

        public override void Write(Utf8JsonWriter writer, GetTaskResult value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            if (value.ResultType is not null)
            {
                writer.WriteString("resultType", value.ResultType);
            }

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
                case CompletedTaskResult completed:
                    writer.WritePropertyName("result");
                    completed.Result.WriteTo(writer);
                    break;
                case FailedTaskResult failed:
                    writer.WritePropertyName("error");
                    failed.Error.WriteTo(writer);
                    break;
                case InputRequiredTaskResult inputRequired:
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
/// Represents a task that is currently being processed by the server.
/// </summary>
/// <remarks>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md">SEP-2663</see>
/// specification for details.
/// </remarks>
public sealed class WorkingTaskResult : GetTaskResult
{
    /// <inheritdoc/>
    [JsonPropertyName("status")]
    public override McpTaskStatus Status => McpTaskStatus.Working;
}

/// <summary>
/// Represents a task that has completed successfully, carrying the final result.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Result"/> field contains the result structure matching the original request type.
/// For example, a <c>tools/call</c> task would contain the <see cref="CallToolResult"/> structure.
/// This includes tool calls that returned results with <c>isError: true</c>.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md">SEP-2663</see>
/// specification for details.
/// </para>
/// </remarks>
public sealed class CompletedTaskResult : GetTaskResult
{
    /// <inheritdoc/>
    [JsonPropertyName("status")]
    public override McpTaskStatus Status => McpTaskStatus.Completed;

    /// <summary>
    /// Gets or sets the final result of the task as raw JSON.
    /// </summary>
    /// <remarks>
    /// The structure matches the result type of the original request.
    /// </remarks>
    [JsonPropertyName("result")]
    public required JsonElement Result { get; set; }
}

/// <summary>
/// Represents a task that failed due to a JSON-RPC error during execution.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Error"/> field contains the JSON-RPC error object that caused the failure.
/// This status must not be used for non-JSON-RPC errors.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md">SEP-2663</see>
/// specification for details.
/// </para>
/// </remarks>
public sealed class FailedTaskResult : GetTaskResult
{
    /// <inheritdoc/>
    [JsonPropertyName("status")]
    public override McpTaskStatus Status => McpTaskStatus.Failed;

    /// <summary>
    /// Gets or sets the JSON-RPC error that caused the task to fail.
    /// </summary>
    [JsonPropertyName("error")]
    public required JsonElement Error { get; set; }
}

/// <summary>
/// Represents a task that was cancelled before completion.
/// </summary>
/// <remarks>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md">SEP-2663</see>
/// specification for details.
/// </remarks>
public sealed class CancelledTaskResult : GetTaskResult
{
    /// <inheritdoc/>
    [JsonPropertyName("status")]
    public override McpTaskStatus Status => McpTaskStatus.Cancelled;
}

/// <summary>
/// Represents a task that requires input from the client before it can proceed.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="InputRequests"/> field contains outstanding server-to-client requests
/// that the client must fulfil. Each entry is keyed by an arbitrary identifier for matching
/// requests to responses, and each value is an <see cref="InputRequest"/> wrapping the
/// server-to-client request payload.
/// </para>
/// <para>
/// Clients must treat each entry as they would the equivalent standalone server-to-client request.
/// Clients should deduplicate keys across consecutive polls to avoid presenting the same request
/// to the user or model more than once.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md">SEP-2663</see>
/// specification for details.
/// </para>
/// </remarks>
public sealed class InputRequiredTaskResult : GetTaskResult
{
    /// <inheritdoc/>
    [JsonPropertyName("status")]
    public override McpTaskStatus Status => McpTaskStatus.InputRequired;

    /// <summary>
    /// Gets or sets the server-to-client requests that need to be fulfilled.
    /// </summary>
    /// <remarks>
    /// Keys are arbitrary identifiers for matching requests to responses.
    /// Each value is an <see cref="InputRequest"/> wrapping the server-to-client request
    /// (e.g., a sampling, elicitation, or roots-list request).
    /// </remarks>
    [JsonPropertyName("inputRequests")]
    public IDictionary<string, InputRequest>? InputRequests { get; set; }
}
