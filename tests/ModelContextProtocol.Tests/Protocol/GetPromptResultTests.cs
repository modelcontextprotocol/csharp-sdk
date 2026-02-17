using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class GetPromptResultTests
{
    [Fact]
    public static void GetPromptResult_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new GetPromptResult
        {
            Description = "A code review prompt",
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock { Text = "Review this code" }
                },
                new PromptMessage
                {
                    Role = Role.Assistant,
                    Content = new TextContentBlock { Text = "I'll review it" }
                }
            ],
            Meta = new JsonObject { ["key"] = "value" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<GetPromptResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("A code review prompt", deserialized.Description);
        Assert.Equal(2, deserialized.Messages.Count);
        Assert.Equal(Role.User, deserialized.Messages[0].Role);
        var textBlock0 = Assert.IsType<TextContentBlock>(deserialized.Messages[0].Content);
        Assert.Equal("Review this code", textBlock0.Text);
        Assert.Equal(Role.Assistant, deserialized.Messages[1].Role);
        var textBlock1 = Assert.IsType<TextContentBlock>(deserialized.Messages[1].Content);
        Assert.Equal("I'll review it", textBlock1.Text);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("value", (string)deserialized.Meta["key"]!);
    }

    [Fact]
    public static void GetPromptResult_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new GetPromptResult();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<GetPromptResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Description);
        Assert.Empty(deserialized.Messages);
        Assert.Null(deserialized.Meta);
    }
}
