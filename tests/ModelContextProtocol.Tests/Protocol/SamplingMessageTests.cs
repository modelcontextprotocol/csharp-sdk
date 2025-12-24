using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public class SamplingMessageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithToolResults_SerializationRoundtrips(bool materializeUtf8TextContentBlocks)
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

        var options = TextMaterializationTestHelpers.GetOptions(materializeUtf8TextContentBlocks);
        var json = JsonSerializer.Serialize(message, options);
        var deserialized = JsonSerializer.Deserialize<SamplingMessage>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(Role.User, deserialized.Role);
        Assert.Single(deserialized.Content);
        
        var toolResult = Assert.IsType<ToolResultContentBlock>(deserialized.Content[0]);
        Assert.Equal("call_123", toolResult.ToolUseId);
        Assert.Single(toolResult.Content);
        
        Assert.Equal("Weather in Paris: 18°C, partly cloudy", TextMaterializationTestHelpers.GetText(toolResult.Content[0], materializeUtf8TextContentBlocks));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithMultipleToolResults_SerializationRoundtrips(bool materializeUtf8TextContentBlocks)
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

        var options = TextMaterializationTestHelpers.GetOptions(materializeUtf8TextContentBlocks);
        var json = JsonSerializer.Serialize(message, options);
        var deserialized = JsonSerializer.Deserialize<SamplingMessage>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(Role.User, deserialized.Role);
        Assert.Equal(2, deserialized.Content.Count);
        
        var toolResult1 = Assert.IsType<ToolResultContentBlock>(deserialized.Content[0]);
        Assert.Equal("call_abc123", toolResult1.ToolUseId);
        Assert.Single(toolResult1.Content);
        Assert.Equal("Weather in Paris: 18°C, partly cloudy", TextMaterializationTestHelpers.GetText(toolResult1.Content[0], materializeUtf8TextContentBlocks));
        
        var toolResult2 = Assert.IsType<ToolResultContentBlock>(deserialized.Content[1]);
        Assert.Equal("call_def456", toolResult2.ToolUseId);
        Assert.Single(toolResult2.Content);
        Assert.Equal("Weather in London: 15°C, rainy", TextMaterializationTestHelpers.GetText(toolResult2.Content[0], materializeUtf8TextContentBlocks));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithToolResultOnly_SerializationRoundtrips(bool materializeUtf8TextContentBlocks)
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

        var options = TextMaterializationTestHelpers.GetOptions(materializeUtf8TextContentBlocks);
        var json = JsonSerializer.Serialize(message, options);
        var deserialized = JsonSerializer.Deserialize<SamplingMessage>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(Role.User, deserialized.Role);
        Assert.Single(deserialized.Content);
        var toolResult = Assert.IsType<ToolResultContentBlock>(deserialized.Content[0]);
        Assert.Equal("call_123", toolResult.ToolUseId);
        Assert.Single(toolResult.Content);
        Assert.Equal("Result", TextMaterializationTestHelpers.GetText(toolResult.Content[0], materializeUtf8TextContentBlocks));
    }
}