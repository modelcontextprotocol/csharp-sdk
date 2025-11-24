using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class McpTaskTests
{
    [Fact]
    public static void McpTask_SerializationRoundTrip_PreservesAllProperties()
    {
        // Arrange
        var original = new McpTask
        {
            TaskId = "task-12345",
            Status = McpTaskStatus.Working,
            StatusMessage = "Processing request",
            CreatedAt = new DateTimeOffset(2025, 12, 9, 10, 30, 0, TimeSpan.Zero),
            LastUpdatedAt = new DateTimeOffset(2025, 12, 9, 10, 35, 0, TimeSpan.Zero),
            TimeToLive = TimeSpan.FromHours(24),
            PollInterval = TimeSpan.FromSeconds(5)
        };

        // Act - Serialize to JSON
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);

        // Act - Deserialize back from JSON
        var deserialized = JsonSerializer.Deserialize<McpTask>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.TaskId, deserialized.TaskId);
        Assert.Equal(original.Status, deserialized.Status);
        Assert.Equal(original.StatusMessage, deserialized.StatusMessage);
        Assert.Equal(original.CreatedAt, deserialized.CreatedAt);
        Assert.Equal(original.LastUpdatedAt, deserialized.LastUpdatedAt);
        Assert.Equal(original.TimeToLive, deserialized.TimeToLive);
        Assert.Equal(original.PollInterval, deserialized.PollInterval);
    }

    [Fact]
    public static void McpTask_SerializationRoundTrip_WithMinimalProperties()
    {
        // Arrange
        var original = new McpTask
        {
            TaskId = "task-minimal",
            Status = McpTaskStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow
        };

        // Act - Serialize to JSON
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);

        // Act - Deserialize back from JSON
        var deserialized = JsonSerializer.Deserialize<McpTask>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.TaskId, deserialized.TaskId);
        Assert.Equal(original.Status, deserialized.Status);
        Assert.Null(deserialized.StatusMessage);
        Assert.Equal(original.CreatedAt, deserialized.CreatedAt);
        Assert.Equal(original.LastUpdatedAt, deserialized.LastUpdatedAt);
        Assert.Null(deserialized.TimeToLive);
        Assert.Null(deserialized.PollInterval);
    }

    [Fact]
    public static void McpTask_HasCorrectJsonPropertyNames()
    {
        var task = new McpTask
        {
            TaskId = "test-task",
            Status = McpTaskStatus.Working,
            StatusMessage = "Test message",
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            TimeToLive = TimeSpan.FromMinutes(30),
            PollInterval = TimeSpan.FromSeconds(1)
        };

        string json = JsonSerializer.Serialize(task, McpJsonUtilities.DefaultOptions);

        Assert.Contains("\"taskId\":", json);
        Assert.Contains("\"status\":", json);
        Assert.Contains("\"statusMessage\":", json);
        Assert.Contains("\"createdAt\":", json);
        Assert.Contains("\"lastUpdatedAt\":", json);
        Assert.Contains("\"ttl\":", json);
        Assert.Contains("\"pollInterval\":", json);
    }

    [Fact]
    public static void McpTask_TimeToLive_SerializesAsMilliseconds()
    {
        var task = new McpTask
        {
            TaskId = "test-ttl",
            Status = McpTaskStatus.Working,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            TimeToLive = TimeSpan.FromSeconds(60)
        };

        string json = JsonSerializer.Serialize(task, McpJsonUtilities.DefaultOptions);

        Assert.Contains("\"ttl\":60000", json);
    }

    [Theory]
    [InlineData(McpTaskStatus.Working)]
    [InlineData(McpTaskStatus.InputRequired)]
    [InlineData(McpTaskStatus.Completed)]
    [InlineData(McpTaskStatus.Failed)]
    [InlineData(McpTaskStatus.Cancelled)]
    public static void McpTaskStatus_SerializesCorrectly(McpTaskStatus status)
    {
        var task = new McpTask
        {
            TaskId = "status-test",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow
        };

        string json = JsonSerializer.Serialize(task, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<McpTask>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(status, deserialized.Status);
    }

    [Fact]
    public static void McpTaskStatus_HasCorrectJsonValues()
    {
        var statuses = new[]
        {
            (McpTaskStatus.Working, "working"),
            (McpTaskStatus.InputRequired, "input_required"),
            (McpTaskStatus.Completed, "completed"),
            (McpTaskStatus.Failed, "failed"),
            (McpTaskStatus.Cancelled, "cancelled")
        };

        foreach (var (status, expectedJson) in statuses)
        {
            var task = new McpTask
            {
                TaskId = "test",
                Status = status,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdatedAt = DateTimeOffset.UtcNow
            };

            string json = JsonSerializer.Serialize(task, McpJsonUtilities.DefaultOptions);
            Assert.Contains($"\"status\":\"{expectedJson}\"", json);
        }
    }

    [Fact]
    public static void McpTaskMetadata_SerializationRoundTrip_WithTimeToLive()
    {
        // Arrange
        var original = new McpTaskMetadata
        {
            TimeToLive = TimeSpan.FromHours(2)
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<McpTaskMetadata>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.TimeToLive, deserialized.TimeToLive);
    }

    [Fact]
    public static void McpTaskMetadata_SerializationRoundTrip_WithNullTimeToLive()
    {
        // Arrange
        var original = new McpTaskMetadata();

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<McpTaskMetadata>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Null(deserialized.TimeToLive);
    }

    [Fact]
    public static void McpTaskMetadata_HasCorrectJsonPropertyNames()
    {
        var metadata = new McpTaskMetadata
        {
            TimeToLive = TimeSpan.FromMinutes(15)
        };

        string json = JsonSerializer.Serialize(metadata, McpJsonUtilities.DefaultOptions);

        Assert.Contains("\"ttl\":", json);
    }

    [Fact]
    public static void McpTaskStatusNotificationParams_SerializationRoundTrip()
    {
        // Arrange
        var original = new McpTaskStatusNotificationParams
        {
            TaskId = "notification-task",
            Status = McpTaskStatus.Completed,
            StatusMessage = "Task completed successfully",
            CreatedAt = new DateTimeOffset(2025, 12, 9, 10, 0, 0, TimeSpan.Zero),
            LastUpdatedAt = new DateTimeOffset(2025, 12, 9, 10, 30, 0, TimeSpan.Zero),
            TimeToLive = TimeSpan.FromHours(1),
            PollInterval = TimeSpan.FromSeconds(2)
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<McpTaskStatusNotificationParams>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.TaskId, deserialized.TaskId);
        Assert.Equal(original.Status, deserialized.Status);
        Assert.Equal(original.StatusMessage, deserialized.StatusMessage);
        Assert.Equal(original.CreatedAt, deserialized.CreatedAt);
        Assert.Equal(original.LastUpdatedAt, deserialized.LastUpdatedAt);
        Assert.Equal(original.TimeToLive, deserialized.TimeToLive);
        Assert.Equal(original.PollInterval, deserialized.PollInterval);
    }

    [Fact]
    public static void GetTaskRequestParams_SerializationRoundTrip()
    {
        // Arrange
        var original = new GetTaskRequestParams
        {
            TaskId = "get-task-123"
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<GetTaskRequestParams>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.TaskId, deserialized.TaskId);
    }

    [Fact]
    public static void GetTaskResult_SerializationRoundTrip()
    {
        // Arrange
        var original = new GetTaskResult
        {
            TaskId = "result-123",
            Status = McpTaskStatus.Completed,
            StatusMessage = "Done",
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            TimeToLive = TimeSpan.FromHours(1),
            PollInterval = TimeSpan.FromSeconds(1)
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<GetTaskResult>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.TaskId, deserialized.TaskId);
        Assert.Equal(original.Status, deserialized.Status);
        Assert.Equal(original.StatusMessage, deserialized.StatusMessage);
        Assert.Equal(original.CreatedAt, deserialized.CreatedAt);
        Assert.Equal(original.LastUpdatedAt, deserialized.LastUpdatedAt);
        Assert.Equal(original.TimeToLive, deserialized.TimeToLive);
        Assert.Equal(original.PollInterval, deserialized.PollInterval);
    }

    [Fact]
    public static void CancelMcpTaskRequestParams_SerializationRoundTrip()
    {
        // Arrange
        var original = new CancelMcpTaskRequestParams
        {
            TaskId = "cancel-task-456"
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CancelMcpTaskRequestParams>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.TaskId, deserialized.TaskId);
    }

    [Fact]
    public static void CancelMcpTaskResult_SerializationRoundTrip()
    {
        // Arrange
        var original = new CancelMcpTaskResult
        {
            TaskId = "cancelled-789",
            Status = McpTaskStatus.Cancelled,
            StatusMessage = "Cancelled by user",
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            TimeToLive = null,
            PollInterval = null
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CancelMcpTaskResult>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.TaskId, deserialized.TaskId);
        Assert.Equal(original.Status, deserialized.Status);
        Assert.Equal(original.StatusMessage, deserialized.StatusMessage);
    }

    [Fact]
    public static void ListTasksRequestParams_SerializationRoundTrip()
    {
        // Arrange
        var original = new ListTasksRequestParams
        {
            Cursor = "cursor-abc123"
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListTasksRequestParams>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Cursor, deserialized.Cursor);
    }

    [Fact]
    public static void ListTasksResult_SerializationRoundTrip()
    {
        // Arrange
        var original = new ListTasksResult
        {
            Tasks =
            [
                new McpTask
                {
                    TaskId = "task-1",
                    Status = McpTaskStatus.Working,
                    CreatedAt = DateTimeOffset.UtcNow,
                    LastUpdatedAt = DateTimeOffset.UtcNow
                },
                new McpTask
                {
                    TaskId = "task-2",
                    Status = McpTaskStatus.Completed,
                    CreatedAt = DateTimeOffset.UtcNow,
                    LastUpdatedAt = DateTimeOffset.UtcNow
                }
            ],
            NextCursor = "next-page-token"
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListTasksResult>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Tasks);
        Assert.Equal(2, deserialized.Tasks.Length);
        Assert.Equal(original.Tasks[0].TaskId, deserialized.Tasks[0].TaskId);
        Assert.Equal(original.Tasks[1].TaskId, deserialized.Tasks[1].TaskId);
        Assert.Equal(original.NextCursor, deserialized.NextCursor);
    }

    [Fact]
    public static void GetTaskPayloadRequestParams_SerializationRoundTrip()
    {
        // Arrange
        var original = new GetTaskPayloadRequestParams
        {
            TaskId = "payload-task-999"
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<GetTaskPayloadRequestParams>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.TaskId, deserialized.TaskId);
    }

    [Fact]
    public static void McpTasksCapability_SerializationRoundTrip_WithAllProperties()
    {
        // Arrange
        var original = new McpTasksCapability
        {
            List = new ListMcpTasksCapability(),
            Cancel = new CancelMcpTasksCapability(),
            Requests = new RequestMcpTasksCapability
            {
                Tools = new ToolsMcpTasksCapability
                {
                    Call = new CallToolMcpTasksCapability()
                },
                Sampling = new SamplingMcpTasksCapability
                {
                    CreateMessage = new CreateMessageMcpTasksCapability()
                },
                Elicitation = new ElicitationMcpTasksCapability
                {
                    Create = new CreateElicitationMcpTasksCapability()
                }
            }
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<McpTasksCapability>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.List);
        Assert.NotNull(deserialized.Cancel);
        Assert.NotNull(deserialized.Requests);
        Assert.NotNull(deserialized.Requests.Tools);
        Assert.NotNull(deserialized.Requests.Tools.Call);
        Assert.NotNull(deserialized.Requests.Sampling);
        Assert.NotNull(deserialized.Requests.Sampling.CreateMessage);
        Assert.NotNull(deserialized.Requests.Elicitation);
        Assert.NotNull(deserialized.Requests.Elicitation.Create);
    }

    [Fact]
    public static void McpTasksCapability_SerializationRoundTrip_WithMinimalProperties()
    {
        // Arrange
        var original = new McpTasksCapability();

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<McpTasksCapability>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Null(deserialized.List);
        Assert.Null(deserialized.Cancel);
        Assert.Null(deserialized.Requests);
    }

    [Fact]
    public static void McpTasksCapability_HasCorrectJsonPropertyNames()
    {
        var capability = new McpTasksCapability
        {
            List = new ListMcpTasksCapability(),
            Cancel = new CancelMcpTasksCapability(),
            Requests = new RequestMcpTasksCapability
            {
                Tools = new ToolsMcpTasksCapability
                {
                    Call = new CallToolMcpTasksCapability()
                }
            }
        };

        string json = JsonSerializer.Serialize(capability, McpJsonUtilities.DefaultOptions);

        Assert.Contains("\"list\":", json);
        Assert.Contains("\"cancel\":", json);
        Assert.Contains("\"requests\":", json);
        Assert.Contains("\"tools\":", json);
        Assert.Contains("\"call\":", json);
    }

    [Fact]
    public static void RequestMcpTasksCapability_SerializationRoundTrip_ToolsOnly()
    {
        // Arrange
        var original = new RequestMcpTasksCapability
        {
            Tools = new ToolsMcpTasksCapability
            {
                Call = new CallToolMcpTasksCapability()
            }
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<RequestMcpTasksCapability>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Tools);
        Assert.NotNull(deserialized.Tools.Call);
        Assert.Null(deserialized.Sampling);
        Assert.Null(deserialized.Elicitation);
    }

    [Fact]
    public static void RequestMcpTasksCapability_SerializationRoundTrip_SamplingOnly()
    {
        // Arrange
        var original = new RequestMcpTasksCapability
        {
            Sampling = new SamplingMcpTasksCapability
            {
                CreateMessage = new CreateMessageMcpTasksCapability()
            }
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<RequestMcpTasksCapability>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Tools);
        Assert.NotNull(deserialized.Sampling);
        Assert.NotNull(deserialized.Sampling.CreateMessage);
        Assert.Null(deserialized.Elicitation);
    }

    [Fact]
    public static void RequestMcpTasksCapability_SerializationRoundTrip_ElicitationOnly()
    {
        // Arrange
        var original = new RequestMcpTasksCapability
        {
            Elicitation = new ElicitationMcpTasksCapability
            {
                Create = new CreateElicitationMcpTasksCapability()
            }
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<RequestMcpTasksCapability>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Tools);
        Assert.Null(deserialized.Sampling);
        Assert.NotNull(deserialized.Elicitation);
        Assert.NotNull(deserialized.Elicitation.Create);
    }

    [Fact]
    public static void RequestMcpTasksCapability_HasCorrectJsonPropertyNames()
    {
        var capability = new RequestMcpTasksCapability
        {
            Tools = new ToolsMcpTasksCapability
            {
                Call = new CallToolMcpTasksCapability()
            },
            Sampling = new SamplingMcpTasksCapability
            {
                CreateMessage = new CreateMessageMcpTasksCapability()
            },
            Elicitation = new ElicitationMcpTasksCapability
            {
                Create = new CreateElicitationMcpTasksCapability()
            }
        };

        string json = JsonSerializer.Serialize(capability, McpJsonUtilities.DefaultOptions);

        Assert.Contains("\"tools\":", json);
        Assert.Contains("\"sampling\":", json);
        Assert.Contains("\"elicitation\":", json);
        Assert.Contains("\"call\":", json);
        Assert.Contains("\"createMessage\":", json);
        Assert.Contains("\"create\":", json);
    }
}
