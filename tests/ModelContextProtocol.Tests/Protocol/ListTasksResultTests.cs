using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class ListTasksResultTests
{
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
        Assert.Equal(2, deserialized.Tasks.Count);
        Assert.Equal(original.Tasks[0].TaskId, deserialized.Tasks[0].TaskId);
        Assert.Equal(original.Tasks[1].TaskId, deserialized.Tasks[1].TaskId);
        Assert.Equal(original.NextCursor, deserialized.NextCursor);
    }
}
