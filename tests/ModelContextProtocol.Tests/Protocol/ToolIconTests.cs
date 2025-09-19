using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class ToolIconTests
{
    [Fact]
    public static void Tool_SerializesToJson_WithIcons()
    {
        var tool = new Tool
        {
            Name = "get_weather",
            Title = "Get Weather",
            Description = "Get current weather information",
            Icons = new List<Icon>
            {
                new() { Src = "https://example.com/weather.png", MimeType = "image/png", Sizes = "48x48" }
            }
        };

        string json = JsonSerializer.Serialize(tool);
        var result = JsonSerializer.Deserialize<Tool>(json);

        Assert.Equal("get_weather", result!.Name);
        Assert.Equal("Get Weather", result.Title);
        Assert.Equal("Get current weather information", result.Description);
        Assert.NotNull(result.Icons);
        Assert.Single(result.Icons);
        Assert.Equal("https://example.com/weather.png", result.Icons[0].Src);
        Assert.Equal("image/png", result.Icons[0].MimeType);
        Assert.Equal("48x48", result.Icons[0].Sizes);
    }

    [Fact]
    public static void Tool_SerializesToJson_WithoutIcons()
    {
        var tool = new Tool
        {
            Name = "calculate",
            Description = "Perform calculations"
        };

        string json = JsonSerializer.Serialize(tool);
        var result = JsonSerializer.Deserialize<Tool>(json);

        Assert.Equal("calculate", result!.Name);
        Assert.Equal("Perform calculations", result.Description);
        Assert.Null(result.Icons);
    }

    [Fact]
    public static void Tool_IconsProperty_HasCorrectJsonPropertyName()
    {
        var tool = new Tool
        {
            Name = "test_tool",
            Icons = new List<Icon> { new() { Src = "https://example.com/icon.png" } }
        };

        string json = JsonSerializer.Serialize(tool);
        Assert.Contains("\"icons\":", json);
    }
}