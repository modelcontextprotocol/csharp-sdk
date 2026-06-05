using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public class CreateMessageResultTests
{
    [Fact]
    public void CreateMessageResult_WithSingleContent_Serializes()
    {
        CreateMessageResult result = new()
        {
            Role = Role.Assistant,
            Model = "test-model",
            Content = [new TextContentBlock { Text = "Hello" }],
            StopReason = "endTurn"
        };

        var json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CreateMessageResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized.Content);
        Assert.IsType<TextContentBlock>(deserialized.Content[0]);
    }

    [Fact]
    public void CreateMessageResult_WithMultipleToolUses_Serializes()
    {
        CreateMessageResult result = new()
        {
            Role = Role.Assistant,
            Model = "test-model",
            Content =
            [
                new ToolUseContentBlock
                {
                    Id = "call_1",
                    Name = "tool1",
                    Input = JsonElement.Parse("""{}""")
                },
                new ToolUseContentBlock
                {
                    Id = "call_2",
                    Name = "tool2",
                    Input = JsonElement.Parse("""{}""")
                }
            ],
            StopReason = "toolUse"
        };

        var json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CreateMessageResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Content.Count);
        Assert.All(deserialized.Content, c => Assert.IsType<ToolUseContentBlock>(c));
        Assert.Equal("call_1", ((ToolUseContentBlock)deserialized.Content[0]).Id);
        Assert.Equal("call_2", ((ToolUseContentBlock)deserialized.Content[1]).Id);
    }

    [Fact]
    public void CreateMessageResult_WithMixedContent_Serializes()
    {
        CreateMessageResult result = new()
        {
            Role = Role.Assistant,
            Model = "test-model",
            Content =
            [
                new TextContentBlock { Text = "Let me check that." },
                new ToolUseContentBlock
                {
                    Id = "call_1",
                    Name = "tool1",
                    Input = JsonElement.Parse("""{}""")
                }
            ],
            StopReason = "toolUse"
        };

        var json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CreateMessageResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Content.Count);
        Assert.IsType<TextContentBlock>(deserialized.Content[0]);
        Assert.IsType<ToolUseContentBlock>(deserialized.Content[1]);
    }

    [Fact]
    public void CreateMessageResult_EmptyContent_AllowedButUnusual()
    {
        CreateMessageResult result = new()
        {
            Role = Role.Assistant,
            Model = "test-model",
            Content = [],
            StopReason = "endTurn"
        };

        var json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CreateMessageResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.Content);
    }

    [Fact]
    public void CreateMessageResult_WithImageContent_Serializes()
    {
        CreateMessageResult result = new()
        {
            Role = Role.Assistant,
            Model = "test-model",
            Content =
            [
                ImageContentBlock.FromBytes((byte[])[1, 2, 3, 4, 5], "image/png")
            ],
            StopReason = "endTurn"
        };

        var json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CreateMessageResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized.Content);
        var imageBlock = Assert.IsType<ImageContentBlock>(deserialized.Content[0]);
        Assert.Equal("image/png", imageBlock.MimeType);
    }

    [Fact]
    public void CreateMessageResult_RoundTripWithAllFields()
    {
        CreateMessageResult original = new()
        {
            Role = Role.Assistant,
            Model = "claude-3-sonnet",
            Content =
            [
                new TextContentBlock { Text = "I'll help you with that." },
                new ToolUseContentBlock
                {
                    Id = "call_xyz",
                    Name = "calculator",
                    Input = JsonElement.Parse("""{"operation":"add","a":5,"b":3}""")
                }
            ],
            StopReason = "toolUse",
            Meta = (JsonObject)JsonNode.Parse("""{"custom":"metadata"}""")!
        };

        var json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CreateMessageResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(Role.Assistant, deserialized.Role);
        Assert.Equal("claude-3-sonnet", deserialized.Model);
        Assert.Equal(2, deserialized.Content.Count);
        Assert.Equal("toolUse", deserialized.StopReason);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("metadata", (string)deserialized.Meta["custom"]!);
    }

    [Fact]
    public void CreateMessageResult_WithToolUse_SerializationRoundtrips()
    {
        CreateMessageResult result = new()
        {
            Role = Role.Assistant,
            Model = "test-model",
            Content =
            [
                new ToolUseContentBlock
                {
                    Id = "call_123",
                    Name = "get_weather",
                    Input = JsonElement.Parse("""{"city":"Paris"}""")
                }
            ],
            StopReason = "toolUse"
        };

        var json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CreateMessageResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(Role.Assistant, deserialized.Role);
        Assert.Equal("test-model", deserialized.Model);
        Assert.Equal("toolUse", deserialized.StopReason);
        Assert.Single(deserialized.Content);

        var toolUse = Assert.IsType<ToolUseContentBlock>(deserialized.Content[0]);
        Assert.Equal("call_123", toolUse.Id);
        Assert.Equal("get_weather", toolUse.Name);
        Assert.Equal("Paris", toolUse.Input.GetProperty("city").GetString());
    }

    [Fact]
    public void CreateMessageResult_WithParallelToolUses_SerializationRoundtrips()
    {
        CreateMessageResult result = new()
        {
            Role = Role.Assistant,
            Model = "test-model",
            Content =
            [
                new ToolUseContentBlock
                {
                    Id = "call_abc123",
                    Name = "get_weather",
                    Input = JsonElement.Parse("""{"city":"Paris"}""")
                },
                new ToolUseContentBlock
                {
                    Id = "call_def456",
                    Name = "get_weather",
                    Input = JsonElement.Parse("""{"city":"London"}""")
                }
            ],
            StopReason = "toolUse"
        };

        var json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CreateMessageResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(Role.Assistant, deserialized.Role);
        Assert.Equal("test-model", deserialized.Model);
        Assert.Equal("toolUse", deserialized.StopReason);
        Assert.Equal(2, deserialized.Content.Count);

        var toolUse1 = Assert.IsType<ToolUseContentBlock>(deserialized.Content[0]);
        Assert.Equal("call_abc123", toolUse1.Id);
        Assert.Equal("get_weather", toolUse1.Name);
        Assert.Equal("Paris", toolUse1.Input.GetProperty("city").GetString());

        var toolUse2 = Assert.IsType<ToolUseContentBlock>(deserialized.Content[1]);
        Assert.Equal("call_def456", toolUse2.Id);
        Assert.Equal("get_weather", toolUse2.Name);
        Assert.Equal("London", toolUse2.Input.GetProperty("city").GetString());
    }
}
