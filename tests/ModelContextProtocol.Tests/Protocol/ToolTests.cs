using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class ToolTests
{
    [Fact]
    public static void Tool_SerializationRoundTrip_PreservesAllProperties()
    {
        // Arrange
        var original = new Tool
        {
            Name = "get_weather",
            Title = "Get Weather",
            Description = "Get current weather information",
            Icons =
            [
                new() { Src = "https://example.com/weather.png", MimeType = "image/png", Sizes = "48x48" }
            ],
            Annotations = new ToolAnnotations
            {
                Title = "Weather Tool",
                ReadOnlyHint = true
            }
        };

        // Act - Serialize to JSON
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        
        // Act - Deserialize back from JSON
        var deserialized = JsonSerializer.Deserialize<Tool>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Title, deserialized.Title);
        Assert.Equal(original.Description, deserialized.Description);
        Assert.NotNull(deserialized.Icons);
        Assert.Equal(original.Icons.Count, deserialized.Icons.Count);
        Assert.Equal(original.Icons[0].Src, deserialized.Icons[0].Src);
        Assert.Equal(original.Icons[0].MimeType, deserialized.Icons[0].MimeType);
        Assert.Equal(original.Icons[0].Sizes, deserialized.Icons[0].Sizes);
        Assert.NotNull(deserialized.Annotations);
        Assert.Equal(original.Annotations.Title, deserialized.Annotations.Title);
        Assert.Equal(original.Annotations.ReadOnlyHint, deserialized.Annotations.ReadOnlyHint);
    }

    [Fact]
    public static void Tool_SerializationRoundTrip_WithMinimalProperties()
    {
        // Arrange
        var original = new Tool
        {
            Name = "calculate"
        };

        // Act - Serialize to JSON
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        
        // Act - Deserialize back from JSON
        var deserialized = JsonSerializer.Deserialize<Tool>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Title, deserialized.Title);
        Assert.Equal(original.Description, deserialized.Description);
        Assert.Equal(original.Icons, deserialized.Icons);
        Assert.Equal(original.Annotations, deserialized.Annotations);
    }

    [Fact]
    public static void Tool_HasCorrectJsonPropertyNames()
    {
        var tool = new Tool
        {
            Name = "test_tool",
            Title = "Test Tool",
            Description = "A test tool",
            Icons = [new() { Src = "https://example.com/icon.png" }],
            Annotations = new ToolAnnotations { Title = "Annotation Title" }
        };

        string json = JsonSerializer.Serialize(tool, McpJsonUtilities.DefaultOptions);

        Assert.Contains("\"name\":", json);
        Assert.Contains("\"title\":", json);
        Assert.Contains("\"description\":", json);
        Assert.Contains("\"icons\":", json);
        Assert.Contains("\"annotations\":", json);
        Assert.Contains("\"inputSchema\":", json);
    }
}
