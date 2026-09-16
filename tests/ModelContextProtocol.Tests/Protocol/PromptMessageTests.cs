using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class PromptMessageTests
{
    [Fact]
    public static void PromptMessage_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new PromptMessage
        {
            Role = Role.User,
            Content = new TextContentBlock { Text = "Hello, world!" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<PromptMessage>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(Role.User, deserialized.Role);
        var textBlock = Assert.IsType<TextContentBlock>(deserialized.Content);
        Assert.Equal("Hello, world!", textBlock.Text);
    }

    [Fact]
    public static void PromptMessage_SerializationRoundTrip_WithImageContent()
    {
        var original = new PromptMessage
        {
            Role = Role.Assistant,
            Content = new ImageContentBlock { Data = System.Text.Encoding.UTF8.GetBytes("base64data"), MimeType = "image/png" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<PromptMessage>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(Role.Assistant, deserialized.Role);
        var imageBlock = Assert.IsType<ImageContentBlock>(deserialized.Content);
        Assert.Equal("base64data", System.Text.Encoding.UTF8.GetString(imageBlock.Data.ToArray()));
        Assert.Equal("image/png", imageBlock.MimeType);
    }
}
