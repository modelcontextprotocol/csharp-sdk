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
}
