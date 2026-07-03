using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

/// <summary>
/// Serialization and deserialization tests for SEP-2663 task protocol types.
/// </summary>
public static class TaskSerializationTests
{
    #region CreateTaskResult

    [Fact]
    public static void CreateTaskResult_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new CreateTaskResult
        {
            TaskId = "task-123",
            Status = McpTaskStatus.Working,
            StatusMessage = "Processing...",
            CreatedAt = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero),
            LastUpdatedAt = new DateTimeOffset(2025, 6, 1, 12, 5, 0, TimeSpan.Zero),
            TimeToLive = TimeSpan.FromHours(1),
            PollIntervalMs = 5000,
            ResultType = "task",
            Meta = new JsonObject { ["key"] = "value" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CreateTaskResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("task-123", deserialized.TaskId);
        Assert.Equal(McpTaskStatus.Working, deserialized.Status);
        Assert.Equal("Processing...", deserialized.StatusMessage);
        Assert.Equal(original.CreatedAt, deserialized.CreatedAt);
        Assert.Equal(original.LastUpdatedAt, deserialized.LastUpdatedAt);
        Assert.Equal(TimeSpan.FromHours(1), deserialized.TimeToLive);
        Assert.Equal(5000, deserialized.PollIntervalMs);
        Assert.Equal("task", deserialized.ResultType);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("value", (string)deserialized.Meta["key"]!);
    }

    [Fact]
    public static void CreateTaskResult_UsesCorrectWireFieldNames()
    {
        var result = new CreateTaskResult
        {
            TaskId = "t1",
            Status = McpTaskStatus.Working,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            TimeToLive = TimeSpan.FromMinutes(1),
            PollIntervalMs = 1000,
            ResultType = "task",
        };

        string json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);

        // Must use camelCase wire names
        Assert.Contains("\"ttlMs\":", json);
        Assert.Contains("\"pollIntervalMs\":", json);
        Assert.Contains("\"taskId\":", json);
        Assert.Contains("\"resultType\":\"task\"", json);

        // Must NOT contain legacy field names
        Assert.DoesNotContain("\"ttl\":", json);
        Assert.DoesNotContain("\"pollInterval\":", json);
    }

    [Fact]
    public static void CreateTaskResult_ResultType_SerializesAsTask()
    {
        var result = new CreateTaskResult
        {
            TaskId = "t1",
            Status = McpTaskStatus.Working,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            ResultType = "task",
        };

        string json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        var node = JsonNode.Parse(json)!;

        Assert.Equal("task", (string)node["resultType"]!);
    }

    #endregion

    #region GetTaskResult Subtypes

    [Fact]
    public static void GetTaskResult_Working_RoundTrip()
    {
        var original = new WorkingTaskResult
        {
            TaskId = "w1",
            CreatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            LastUpdatedAt = new DateTimeOffset(2025, 1, 1, 0, 1, 0, TimeSpan.Zero),
            StatusMessage = "In progress",
            PollIntervalMs = 2000,
        };

        string json = JsonSerializer.Serialize<GetTaskResult>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<GetTaskResult>(json, McpJsonUtilities.DefaultOptions);

        var working = Assert.IsType<WorkingTaskResult>(deserialized);
        Assert.Equal("w1", working.TaskId);
        Assert.Equal(McpTaskStatus.Working, working.Status);
        Assert.Equal("In progress", working.StatusMessage);
        Assert.Equal(2000, working.PollIntervalMs);
    }

    [Fact]
    public static void GetTaskResult_Completed_RoundTrip_IncludesResult()
    {
        var resultPayload = JsonElement.Parse("""{"content":[{"type":"text","text":"done"}]}""");
        var original = new CompletedTaskResult
        {
            TaskId = "c1",
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            Result = resultPayload,
        };

        string json = JsonSerializer.Serialize<GetTaskResult>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<GetTaskResult>(json, McpJsonUtilities.DefaultOptions);

        var completed = Assert.IsType<CompletedTaskResult>(deserialized);
        Assert.Equal("c1", completed.TaskId);
        Assert.Equal(McpTaskStatus.Completed, completed.Status);
        Assert.Equal(JsonValueKind.Object, completed.Result.ValueKind);
    }

    [Fact]
    public static void GetTaskResult_Failed_RoundTrip_IncludesError()
    {
        var errorPayload = JsonElement.Parse("""{"code":-32000,"message":"internal error"}""");
        var original = new FailedTaskResult
        {
            TaskId = "f1",
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            Error = errorPayload,
        };

        string json = JsonSerializer.Serialize<GetTaskResult>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<GetTaskResult>(json, McpJsonUtilities.DefaultOptions);

        var failed = Assert.IsType<FailedTaskResult>(deserialized);
        Assert.Equal("f1", failed.TaskId);
        Assert.Equal(McpTaskStatus.Failed, failed.Status);
        Assert.Equal(-32000, failed.Error.GetProperty("code").GetInt32());
    }

    [Fact]
    public static void GetTaskResult_Cancelled_RoundTrip()
    {
        var original = new CancelledTaskResult
        {
            TaskId = "x1",
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            StatusMessage = "User cancelled",
        };

        string json = JsonSerializer.Serialize<GetTaskResult>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<GetTaskResult>(json, McpJsonUtilities.DefaultOptions);

        var cancelled = Assert.IsType<CancelledTaskResult>(deserialized);
        Assert.Equal("x1", cancelled.TaskId);
        Assert.Equal(McpTaskStatus.Cancelled, cancelled.Status);
        Assert.Equal("User cancelled", cancelled.StatusMessage);
    }

    [Fact]
    public static void GetTaskResult_InputRequired_RoundTrip_IncludesInputRequests()
    {
        var inputRequests = new Dictionary<string, InputRequest>
        {
            ["req-1"] = new InputRequest
            {
                Method = "elicitation/create",
                Params = JsonElement.Parse("""{"message":"Confirm?"}"""),
            }
        };
        var original = new InputRequiredTaskResult
        {
            TaskId = "i1",
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            InputRequests = inputRequests,
        };

        string json = JsonSerializer.Serialize<GetTaskResult>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<GetTaskResult>(json, McpJsonUtilities.DefaultOptions);

        var inputRequired = Assert.IsType<InputRequiredTaskResult>(deserialized);
        Assert.Equal("i1", inputRequired.TaskId);
        Assert.Equal(McpTaskStatus.InputRequired, inputRequired.Status);
        Assert.NotNull(inputRequired.InputRequests);
        Assert.Single(inputRequired.InputRequests);
        Assert.True(inputRequired.InputRequests.ContainsKey("req-1"));
    }

    [Fact]
    public static void GetTaskResult_Converter_DispatchesToCorrectSubtypeByStatus()
    {
        var statuses = new (string status, Type expectedType)[]
        {
            ("working", typeof(WorkingTaskResult)),
            ("completed", typeof(CompletedTaskResult)),
            ("failed", typeof(FailedTaskResult)),
            ("cancelled", typeof(CancelledTaskResult)),
            ("input_required", typeof(InputRequiredTaskResult)),
        };

        foreach (var (status, expectedType) in statuses)
        {
            var json = status switch
            {
                "completed" => """{"taskId":"t","status":"completed","createdAt":"2025-01-01T00:00:00Z","lastUpdatedAt":"2025-01-01T00:00:00Z","result":{}}""",
                "failed" => """{"taskId":"t","status":"failed","createdAt":"2025-01-01T00:00:00Z","lastUpdatedAt":"2025-01-01T00:00:00Z","error":{}}""",
                "input_required" => """{"taskId":"t","status":"input_required","createdAt":"2025-01-01T00:00:00Z","lastUpdatedAt":"2025-01-01T00:00:00Z","inputRequests":{}}""",
                _ => $$$"""{"taskId":"t","status":"{{{status}}}","createdAt":"2025-01-01T00:00:00Z","lastUpdatedAt":"2025-01-01T00:00:00Z"}""",
            };

            var result = JsonSerializer.Deserialize<GetTaskResult>(json, McpJsonUtilities.DefaultOptions);
            Assert.NotNull(result);
            Assert.IsType(expectedType, result);
        }
    }

    [Fact]
    public static void GetTaskResult_MissingTaskId_ThrowsJsonException()
    {
        var json = """{"status":"working","createdAt":"2025-01-01T00:00:00Z","lastUpdatedAt":"2025-01-01T00:00:00Z"}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GetTaskResult>(json, McpJsonUtilities.DefaultOptions));
    }

    [Fact]
    public static void GetTaskResult_MissingStatus_ThrowsJsonException()
    {
        var json = """{"taskId":"t","createdAt":"2025-01-01T00:00:00Z","lastUpdatedAt":"2025-01-01T00:00:00Z"}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GetTaskResult>(json, McpJsonUtilities.DefaultOptions));
    }

    [Fact]
    public static void GetTaskResult_UnknownStatus_ThrowsJsonException()
    {
        var json = """{"taskId":"t","status":"exploded","createdAt":"2025-01-01T00:00:00Z","lastUpdatedAt":"2025-01-01T00:00:00Z"}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GetTaskResult>(json, McpJsonUtilities.DefaultOptions));
    }

    [Fact]
    public static void GetTaskResult_CompletedMissingResult_ThrowsJsonException()
    {
        var json = """{"taskId":"t","status":"completed","createdAt":"2025-01-01T00:00:00Z","lastUpdatedAt":"2025-01-01T00:00:00Z"}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GetTaskResult>(json, McpJsonUtilities.DefaultOptions));
    }

    [Fact]
    public static void GetTaskResult_FailedMissingError_ThrowsJsonException()
    {
        var json = """{"taskId":"t","status":"failed","createdAt":"2025-01-01T00:00:00Z","lastUpdatedAt":"2025-01-01T00:00:00Z"}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GetTaskResult>(json, McpJsonUtilities.DefaultOptions));
    }

    [Fact]
    public static void GetTaskResult_InputRequiredMissingInputRequests_ThrowsJsonException()
    {
        var json = """{"taskId":"t","status":"input_required","createdAt":"2025-01-01T00:00:00Z","lastUpdatedAt":"2025-01-01T00:00:00Z"}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GetTaskResult>(json, McpJsonUtilities.DefaultOptions));
    }

    [Theory]
    [InlineData(typeof(WorkingTaskResult))]
    [InlineData(typeof(CompletedTaskResult))]
    [InlineData(typeof(FailedTaskResult))]
    [InlineData(typeof(CancelledTaskResult))]
    [InlineData(typeof(InputRequiredTaskResult))]
    public static void GetTaskResult_WireResultType_IsComplete_WhenSet(Type subType)
    {
        // SEP-2663: standard task responses (tasks/get, tasks/update, tasks/cancel) use resultType="complete".
        var created = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        GetTaskResult value = subType switch
        {
            Type t when t == typeof(WorkingTaskResult) => new WorkingTaskResult { TaskId = "t", CreatedAt = created, LastUpdatedAt = created, ResultType = "complete" },
            Type t when t == typeof(CompletedTaskResult) => new CompletedTaskResult { TaskId = "t", CreatedAt = created, LastUpdatedAt = created, Result = JsonElement.Parse("""{"ok":true}"""), ResultType = "complete" },
            Type t when t == typeof(FailedTaskResult) => new FailedTaskResult { TaskId = "t", CreatedAt = created, LastUpdatedAt = created, Error = JsonElement.Parse("""{"code":-32603,"message":"boom"}"""), ResultType = "complete" },
            Type t when t == typeof(CancelledTaskResult) => new CancelledTaskResult { TaskId = "t", CreatedAt = created, LastUpdatedAt = created, ResultType = "complete" },
            Type t when t == typeof(InputRequiredTaskResult) => new InputRequiredTaskResult
            {
                TaskId = "t",
                CreatedAt = created,
                LastUpdatedAt = created,
                ResultType = "complete",
                InputRequests = new Dictionary<string, InputRequest>
                {
                    ["k"] = new InputRequest
                    {
                        Method = "test/method",
                        Params = JsonSerializer.SerializeToElement("ask", McpJsonUtilities.DefaultOptions),
                    },
                },
            },
            _ => throw new InvalidOperationException()
        };

        string json = JsonSerializer.Serialize(value, McpJsonUtilities.DefaultOptions);
        var node = JsonNode.Parse(json)!;

        Assert.Equal("complete", (string?)node["resultType"]);
    }

    #endregion

    #region McpTaskStatus Enum

    [Theory]
    [InlineData(McpTaskStatus.Working, "working")]
    [InlineData(McpTaskStatus.InputRequired, "input_required")]
    [InlineData(McpTaskStatus.Completed, "completed")]
    [InlineData(McpTaskStatus.Cancelled, "cancelled")]
    [InlineData(McpTaskStatus.Failed, "failed")]
    public static void McpTaskStatus_SerializesAsSnakeCase(McpTaskStatus status, string expectedWireValue)
    {
        string json = JsonSerializer.Serialize(status, McpJsonUtilities.DefaultOptions);
        Assert.Equal($"\"{expectedWireValue}\"", json);

        var deserialized = JsonSerializer.Deserialize<McpTaskStatus>(json, McpJsonUtilities.DefaultOptions);
        Assert.Equal(status, deserialized);
    }

    #endregion

    #region TaskStatusNotificationParams

    [Fact]
    public static void TaskStatusNotificationParams_Working_RoundTrip()
    {
        var original = new WorkingTaskNotificationParams
        {
            TaskId = "n1",
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            StatusMessage = "Working on it",
        };

        string json = JsonSerializer.Serialize<TaskStatusNotificationParams>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<TaskStatusNotificationParams>(json, McpJsonUtilities.DefaultOptions);

        var working = Assert.IsType<WorkingTaskNotificationParams>(deserialized);
        Assert.Equal("n1", working.TaskId);
        Assert.Equal("Working on it", working.StatusMessage);
    }

    [Fact]
    public static void TaskStatusNotificationParams_Completed_RoundTrip()
    {
        var resultPayload = JsonElement.Parse("""{"text":"done"}""");
        var original = new CompletedTaskNotificationParams
        {
            TaskId = "n2",
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            Result = resultPayload,
        };

        string json = JsonSerializer.Serialize<TaskStatusNotificationParams>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<TaskStatusNotificationParams>(json, McpJsonUtilities.DefaultOptions);

        var completed = Assert.IsType<CompletedTaskNotificationParams>(deserialized);
        Assert.Equal("n2", completed.TaskId);
        Assert.Equal("done", completed.Result.GetProperty("text").GetString());
    }

    [Fact]
    public static void TaskStatusNotificationParams_Failed_RoundTrip()
    {
        var errorPayload = JsonElement.Parse("""{"code":-1,"message":"boom"}""");
        var original = new FailedTaskNotificationParams
        {
            TaskId = "n3",
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            Error = errorPayload,
        };

        string json = JsonSerializer.Serialize<TaskStatusNotificationParams>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<TaskStatusNotificationParams>(json, McpJsonUtilities.DefaultOptions);

        var failed = Assert.IsType<FailedTaskNotificationParams>(deserialized);
        Assert.Equal("n3", failed.TaskId);
        Assert.Equal("boom", failed.Error.GetProperty("message").GetString());
    }

    [Fact]
    public static void TaskStatusNotificationParams_Cancelled_RoundTrip()
    {
        var original = new CancelledTaskNotificationParams
        {
            TaskId = "n4",
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
        };

        string json = JsonSerializer.Serialize<TaskStatusNotificationParams>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<TaskStatusNotificationParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.IsType<CancelledTaskNotificationParams>(deserialized);
    }

    [Fact]
    public static void TaskStatusNotificationParams_InputRequired_RoundTrip()
    {
        var inputRequests = new Dictionary<string, InputRequest>
        {
            ["r1"] = new InputRequest { Method = "sampling/createMessage" }
        };
        var original = new InputRequiredTaskNotificationParams
        {
            TaskId = "n5",
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            InputRequests = inputRequests,
        };

        string json = JsonSerializer.Serialize<TaskStatusNotificationParams>(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<TaskStatusNotificationParams>(json, McpJsonUtilities.DefaultOptions);

        var inputRequired = Assert.IsType<InputRequiredTaskNotificationParams>(deserialized);
        Assert.NotNull(inputRequired.InputRequests);
        Assert.Single(inputRequired.InputRequests);
    }

    #endregion

    #region ResultOrCreatedTask

    [Fact]
    public static void ResultOrCreatedTask_ImplicitConversion_FromResult()
    {
        CallToolResult callResult = new() { Content = [new TextContentBlock { Text = "hi" }] };

        ResultOrCreatedTask<CallToolResult> augmented = callResult;

        Assert.False(augmented.IsTask);
        Assert.Same(callResult, augmented.Result);
        Assert.Null(augmented.TaskCreated);
    }

    [Fact]
    public static void ResultOrCreatedTask_ImplicitConversion_FromCreateTaskResult()
    {
        CreateTaskResult taskCreated = new()
        {
            TaskId = "t1",
            Status = McpTaskStatus.Working,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
        };

        ResultOrCreatedTask<CallToolResult> augmented = taskCreated;

        Assert.True(augmented.IsTask);
        Assert.Same(taskCreated, augmented.TaskCreated);
        Assert.Null(augmented.Result);
    }

    [Fact]
    public static void ResultOrCreatedTask_IsTask_FalseForResult_TrueForTask()
    {
        var result = new ResultOrCreatedTask<CallToolResult>(new CallToolResult());
        var task = new ResultOrCreatedTask<CallToolResult>(new CreateTaskResult
        {
            TaskId = "t",
            Status = McpTaskStatus.Working,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
        });

        Assert.False(result.IsTask);
        Assert.True(task.IsTask);
    }

    #endregion

    #region UpdateTaskResult / CancelTaskResult Wire Format

    [Fact]
    public static void UpdateTaskResult_WireResultType_IsComplete_WhenSet()
    {
        // SEP-2663: tasks/update responses use resultType="complete".
        var result = new UpdateTaskResult { ResultType = "complete" };
        string json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        var node = JsonNode.Parse(json)!;

        Assert.Equal("complete", (string?)node["resultType"]);
    }

    [Fact]
    public static void CancelTaskResult_WireResultType_IsComplete_WhenSet()
    {
        // SEP-2663: tasks/cancel responses use resultType="complete".
        var result = new CancelTaskResult { ResultType = "complete" };
        string json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        var node = JsonNode.Parse(json)!;

        Assert.Equal("complete", (string?)node["resultType"]);
    }

    #endregion
}
