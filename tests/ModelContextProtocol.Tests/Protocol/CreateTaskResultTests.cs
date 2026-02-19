using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class CreateTaskResultTests
{
    [Fact]
    public static void CreateTaskResult_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new CreateTaskResult
        {
            Task = new McpTask
            {
                TaskId = "task-123",
                Status = McpTaskStatus.Working,
                StatusMessage = "Processing",
                CreatedAt = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero),
                LastUpdatedAt = new DateTimeOffset(2025, 6, 1, 12, 5, 0, TimeSpan.Zero),
                TimeToLive = TimeSpan.FromHours(1),
                PollInterval = TimeSpan.FromSeconds(5)
            },
            Meta = new JsonObject { ["key"] = "value" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CreateTaskResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("task-123", deserialized.Task.TaskId);
        Assert.Equal(McpTaskStatus.Working, deserialized.Task.Status);
        Assert.Equal("Processing", deserialized.Task.StatusMessage);
        Assert.Equal(original.Task.CreatedAt, deserialized.Task.CreatedAt);
        Assert.Equal(original.Task.LastUpdatedAt, deserialized.Task.LastUpdatedAt);
        Assert.Equal(original.Task.TimeToLive, deserialized.Task.TimeToLive);
        Assert.Equal(original.Task.PollInterval, deserialized.Task.PollInterval);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("value", (string)deserialized.Meta["key"]!);
    }
}
