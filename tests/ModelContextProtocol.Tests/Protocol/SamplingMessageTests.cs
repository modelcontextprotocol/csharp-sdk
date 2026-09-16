using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public class SamplingMessageTests
{
    [Fact]
    public void WithToolResults_SerializationRoundtrips()
    {
        SamplingMessage message = new()
        {
            Role = Role.User,
            Content =
            [
                new ToolResultContentBlock
                {
                    ToolUseId = "call_123",
                    Content =
                    [
                        new TextContentBlock { Text = "Weather in Paris: 18°C, partly cloudy" }
                    ]
                }
            ]
        };

        var json = JsonSerializer.Serialize(message, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<SamplingMessage>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(Role.User, deserialized.Role);
        Assert.Single(deserialized.Content);
        
        var toolResult = Assert.IsType<ToolResultContentBlock>(deserialized.Content[0]);
        Assert.Equal("call_123", toolResult.ToolUseId);
        Assert.Single(toolResult.Content);
        
        var textBlock = Assert.IsType<TextContentBlock>(toolResult.Content[0]);
        Assert.Equal("Weather in Paris: 18°C, partly cloudy", textBlock.Text);
    }

    [Fact]
    public void WithMultipleToolResults_SerializationRoundtrips()
    {
        SamplingMessage message = new()
        {
            Role = Role.User,
            Content =
            [
                new ToolResultContentBlock
                {
                    ToolUseId = "call_abc123",
                    Content = [new TextContentBlock { Text = "Weather in Paris: 18°C, partly cloudy" }]
                },
                new ToolResultContentBlock
                {
                    ToolUseId = "call_def456",
                    Content = [new TextContentBlock { Text = "Weather in London: 15°C, rainy" }]
                }
            ]
        };

        var json = JsonSerializer.Serialize(message, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<SamplingMessage>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(Role.User, deserialized.Role);
        Assert.Equal(2, deserialized.Content.Count);
        
        var toolResult1 = Assert.IsType<ToolResultContentBlock>(deserialized.Content[0]);
        Assert.Equal("call_abc123", toolResult1.ToolUseId);
        Assert.Single(toolResult1.Content);
        var textBlock1 = Assert.IsType<TextContentBlock>(toolResult1.Content[0]);
        Assert.Equal("Weather in Paris: 18°C, partly cloudy", textBlock1.Text);
        
        var toolResult2 = Assert.IsType<ToolResultContentBlock>(deserialized.Content[1]);
        Assert.Equal("call_def456", toolResult2.ToolUseId);
        Assert.Single(toolResult2.Content);
        var textBlock2 = Assert.IsType<TextContentBlock>(toolResult2.Content[0]);
        Assert.Equal("Weather in London: 15°C, rainy", textBlock2.Text);
    }

    [Fact]
    public void WithToolResultOnly_SerializationRoundtrips()
    {
        SamplingMessage message = new()
        {
            Role = Role.User,
            Content =
            [
                new ToolResultContentBlock
                {
                    ToolUseId = "call_123",
                    Content = [new TextContentBlock { Text = "Result" }]
                }
            ]
        };

        var json = JsonSerializer.Serialize(message, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<SamplingMessage>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(Role.User, deserialized.Role);
        Assert.Single(deserialized.Content);
        var toolResult = Assert.IsType<ToolResultContentBlock>(deserialized.Content[0]);
        Assert.Equal("call_123", toolResult.ToolUseId);
        Assert.Single(toolResult.Content);
        var textBlock = Assert.IsType<TextContentBlock>(toolResult.Content[0]);
        Assert.Equal("Result", textBlock.Text);
    }
}