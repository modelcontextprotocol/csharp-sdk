using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class McpTaskStatusNotificationParamsTests
{
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
}
