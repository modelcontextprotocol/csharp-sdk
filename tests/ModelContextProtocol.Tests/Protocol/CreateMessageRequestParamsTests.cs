using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public class CreateMessageRequestParamsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithTools_SerializationRoundtrips(bool materializeUtf8TextContentBlocks)
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

        var options = TextMaterializationTestHelpers.GetOptions(materializeUtf8TextContentBlocks);
        var json = JsonSerializer.Serialize(requestParams, options);
        var deserialized = JsonSerializer.Deserialize<CreateMessageRequestParams>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(1000, deserialized.MaxTokens);
        Assert.NotNull(deserialized.Messages);
        Assert.Single(deserialized.Messages);
        Assert.Equal(Role.User, deserialized.Messages[0].Role);
        Assert.Single(deserialized.Messages[0].Content);
        Assert.Equal("What's the weather in Paris?", TextMaterializationTestHelpers.GetText(deserialized.Messages[0].Content[0], materializeUtf8TextContentBlocks));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithToolChoiceRequired_SerializationRoundtrips(bool materializeUtf8TextContentBlocks)
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

        var options = TextMaterializationTestHelpers.GetOptions(materializeUtf8TextContentBlocks);
        var json = JsonSerializer.Serialize(requestParams, options);
        var deserialized = JsonSerializer.Deserialize<CreateMessageRequestParams>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(1000, deserialized.MaxTokens);
        Assert.NotNull(deserialized.Messages);
        Assert.Single(deserialized.Messages);
        Assert.Equal(Role.User, deserialized.Messages[0].Role);
        Assert.Single(deserialized.Messages[0].Content);
        Assert.Equal("What's the weather?", TextMaterializationTestHelpers.GetText(deserialized.Messages[0].Content[0], materializeUtf8TextContentBlocks));
        Assert.NotNull(deserialized.Tools);
        Assert.Single(deserialized.Tools);
        Assert.Equal("get_weather", deserialized.Tools[0].Name);
        Assert.Equal("Get weather for a city", deserialized.Tools[0].Description);
        Assert.Equal("object", deserialized.Tools[0].InputSchema.GetProperty("type").GetString());
        Assert.NotNull(deserialized.ToolChoice);
        Assert.Equal("required", deserialized.ToolChoice.Mode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithToolChoiceNone_SerializationRoundtrips(bool materializeUtf8TextContentBlocks)
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

        var options = TextMaterializationTestHelpers.GetOptions(materializeUtf8TextContentBlocks);
        var json = JsonSerializer.Serialize(requestParams, options);
        var deserialized = JsonSerializer.Deserialize<CreateMessageRequestParams>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(1000, deserialized.MaxTokens);
        Assert.NotNull(deserialized.Messages);
        Assert.Single(deserialized.Messages);
        Assert.Equal(Role.User, deserialized.Messages[0].Role);
        Assert.Single(deserialized.Messages[0].Content);
        Assert.Equal("What's the weather in Paris?", TextMaterializationTestHelpers.GetText(deserialized.Messages[0].Content[0], materializeUtf8TextContentBlocks));
        Assert.NotNull(deserialized.Tools);
        Assert.Single(deserialized.Tools);
        Assert.Equal("get_weather", deserialized.Tools[0].Name);
        Assert.Equal("Get weather for a city", deserialized.Tools[0].Description);
        Assert.Equal("object", deserialized.Tools[0].InputSchema.GetProperty("type").GetString());
        Assert.NotNull(deserialized.ToolChoice);
        Assert.Equal("none", deserialized.ToolChoice.Mode);
    }
}





