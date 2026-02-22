using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public class CreateMessageRequestParamsTests
{
    [Fact]
    public void WithTools_SerializationRoundtrips()
    {
        CreateMessageRequestParams requestParams = new()
        {
            MaxTokens = 1000,
            Messages =
            [
                new SamplingMessage
                {
                    Role = Role.User,
                    Content = [new TextContentBlock { Text = "What's the weather in Paris?" }]
                }
            ],
            Tools =
            [
                new Tool
                {
                    Name = "get_weather",
                    Description = "Get weather for a city",
                    InputSchema = JsonElement.Parse("""
                        {
                            "type": "object",
                            "properties": {
                                "city": { "type": "string" }
                            },
                            "required": ["city"]
                        }
                        """)
                }
            ],
            ToolChoice = new ToolChoice { Mode = "auto" }
        };

        var json = JsonSerializer.Serialize(requestParams, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CreateMessageRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(1000, deserialized.MaxTokens);
        Assert.NotNull(deserialized.Messages);
        Assert.Single(deserialized.Messages);
        Assert.Equal(Role.User, deserialized.Messages[0].Role);
        Assert.Single(deserialized.Messages[0].Content);
        var textContent = Assert.IsType<TextContentBlock>(deserialized.Messages[0].Content[0]);
        Assert.Equal("What's the weather in Paris?", textContent.Text);
        Assert.NotNull(deserialized.Tools);
        Assert.Single(deserialized.Tools);
        Assert.Equal("get_weather", deserialized.Tools[0].Name);
        Assert.Equal("Get weather for a city", deserialized.Tools[0].Description);
        Assert.Equal("object", deserialized.Tools[0].InputSchema.GetProperty("type").GetString());
        Assert.True(deserialized.Tools[0].InputSchema.GetProperty("properties").TryGetProperty("city", out var cityProp));
        Assert.Equal("string", cityProp.GetProperty("type").GetString());
        Assert.Single(deserialized.Tools[0].InputSchema.GetProperty("required").EnumerateArray());
        Assert.Equal("city", deserialized.Tools[0].InputSchema.GetProperty("required")[0].GetString());
        Assert.NotNull(deserialized.ToolChoice);
        Assert.Equal("auto", deserialized.ToolChoice.Mode);
    }

    [Fact]
    public void WithToolChoiceRequired_SerializationRoundtrips()
    {
        CreateMessageRequestParams requestParams = new()
        {
            MaxTokens = 1000,
            Messages =
            [
                new SamplingMessage
                {
                    Role = Role.User,
                    Content = [new TextContentBlock { Text = "What's the weather?" }]
                }
            ],
            Tools =
            [
                new Tool
                {
                    Name = "get_weather",
                    Description = "Get weather for a city",
                    InputSchema = JsonElement.Parse("""
                        {
                            "type": "object",
                            "properties": { "city": { "type": "string" } },
                            "required": ["city"]
                        }
                        """)
                }
            ],
            ToolChoice = new ToolChoice { Mode = "required" }
        };

        var json = JsonSerializer.Serialize(requestParams, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CreateMessageRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(1000, deserialized.MaxTokens);
        Assert.NotNull(deserialized.Messages);
        Assert.Single(deserialized.Messages);
        Assert.Equal(Role.User, deserialized.Messages[0].Role);
        Assert.Single(deserialized.Messages[0].Content);
        var textContent = Assert.IsType<TextContentBlock>(deserialized.Messages[0].Content[0]);
        Assert.Equal("What's the weather?", textContent.Text);
        Assert.NotNull(deserialized.Tools);
        Assert.Single(deserialized.Tools);
        Assert.Equal("get_weather", deserialized.Tools[0].Name);
        Assert.Equal("Get weather for a city", deserialized.Tools[0].Description);
        Assert.Equal("object", deserialized.Tools[0].InputSchema.GetProperty("type").GetString());
        Assert.NotNull(deserialized.ToolChoice);
        Assert.Equal("required", deserialized.ToolChoice.Mode);
    }

    [Fact]
    public void WithToolChoiceNone_SerializationRoundtrips()
    {
        CreateMessageRequestParams requestParams = new()
        {
            MaxTokens = 1000,
            Messages =
            [
                new SamplingMessage
                {
                    Role = Role.User,
                    Content = [new TextContentBlock { Text = "What's the weather in Paris?" }]
                }
            ],
            Tools =
            [
                new Tool
                {
                    Name = "get_weather",
                    Description = "Get weather for a city",
                    InputSchema = JsonElement.Parse("""
                        {
                            "type": "object",
                            "properties": { "city": { "type": "string" } },
                            "required": ["city"]
                        }
                        """)
                }
            ],
            ToolChoice = new ToolChoice { Mode = "none" }
        };

        var json = JsonSerializer.Serialize(requestParams, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CreateMessageRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(1000, deserialized.MaxTokens);
        Assert.NotNull(deserialized.Messages);
        Assert.Single(deserialized.Messages);
        Assert.Equal(Role.User, deserialized.Messages[0].Role);
        Assert.Single(deserialized.Messages[0].Content);
        var textContent = Assert.IsType<TextContentBlock>(deserialized.Messages[0].Content[0]);
        Assert.Equal("What's the weather in Paris?", textContent.Text);
        Assert.NotNull(deserialized.Tools);
        Assert.Single(deserialized.Tools);
        Assert.Equal("get_weather", deserialized.Tools[0].Name);
        Assert.Equal("Get weather for a city", deserialized.Tools[0].Description);
        Assert.Equal("object", deserialized.Tools[0].InputSchema.GetProperty("type").GetString());
        Assert.NotNull(deserialized.ToolChoice);
        Assert.Equal("none", deserialized.ToolChoice.Mode);
    }

    [Fact]
    public void WithMetadata_SerializationRoundtrips()
    {
        CreateMessageRequestParams requestParams = new()
        {
            MaxTokens = 500,
            Messages =
            [
                new SamplingMessage
                {
                    Role = Role.User,
                    Content = [new TextContentBlock { Text = "Hello" }]
                }
            ],
            Metadata = new JsonObject
            {
                ["provider"] = "test-provider",
                ["custom_setting"] = 42
            }
        };

        var json = JsonSerializer.Serialize(requestParams, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CreateMessageRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(500, deserialized.MaxTokens);
        Assert.NotNull(deserialized.Metadata);
        Assert.Equal("test-provider", (string?)deserialized.Metadata["provider"]);
        Assert.Equal(42, (int)deserialized.Metadata["custom_setting"]!);
    }
}





