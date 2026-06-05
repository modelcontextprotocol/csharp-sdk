using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class CancelMcpTaskResultTests
{
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
}
