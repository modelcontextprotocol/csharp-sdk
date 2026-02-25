using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class CallToolResultTests
{
    [Fact]
    public static void CallToolResult_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "Result text" }],
            StructuredContent = JsonElement.Parse("""{"temperature":72}"""),
            IsError = false,
            Task = new McpTask
            {
                TaskId = "task-1",
                Status = McpTaskStatus.Completed,
                CreatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                LastUpdatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
            },
            Meta = new JsonObject { ["key"] = "value" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CallToolResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized.Content);
        var textBlock = Assert.IsType<TextContentBlock>(deserialized.Content[0]);
        Assert.Equal("Result text", textBlock.Text);
        Assert.NotNull(deserialized.StructuredContent);
        Assert.Equal(72, deserialized.StructuredContent.Value.GetProperty("temperature").GetInt32());
        Assert.False(deserialized.IsError);
        Assert.NotNull(deserialized.Task);
        Assert.Equal("task-1", deserialized.Task.TaskId);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("value", (string)deserialized.Meta["key"]!);
    }

    [Fact]
    public static void CallToolResult_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new CallToolResult();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CallToolResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.Content);
        Assert.Null(deserialized.StructuredContent);
        Assert.Null(deserialized.IsError);
        Assert.Null(deserialized.Task);
        Assert.Null(deserialized.Meta);
    }
}
