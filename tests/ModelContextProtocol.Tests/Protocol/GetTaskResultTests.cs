using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class GetTaskResultTests
{
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
}
