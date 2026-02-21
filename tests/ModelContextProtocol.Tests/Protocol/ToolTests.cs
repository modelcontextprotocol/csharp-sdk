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
                new() { Source = "https://example.com/weather.png", MimeType = "image/png", Sizes = ["48x48"] }
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
        Assert.Equal(original.Icons[0].Source, deserialized.Icons[0].Source);
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
            Icons = [new() { Source = "https://example.com/icon.png" }],
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

    [Fact]
    public static void ToolInputSchema_HasValidDefaultSchema()
    {
        var tool = new Tool { Name = "test" };
        JsonElement jsonElement = tool.InputSchema;

        Assert.Equal(JsonValueKind.Object, jsonElement.ValueKind);
        Assert.Single(jsonElement.EnumerateObject());
        Assert.True(jsonElement.TryGetProperty("type", out JsonElement typeElement));
        Assert.Equal(JsonValueKind.String, typeElement.ValueKind);
        Assert.Equal("object", typeElement.GetString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("false")]
    [InlineData("true")]
    [InlineData("3.5e3")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"properties":{}}""")]
    [InlineData("""{"type":"number"}""")]
    [InlineData("""{"type":"array"}""")]
    [InlineData("""{"type":["object"]}""")]
    public static void ToolInputSchema_RejectsInvalidSchemaDocuments(string invalidSchema)
    {
        using var document = JsonDocument.Parse(invalidSchema);
        var tool = new Tool { Name = "test" };

        Assert.Throws<ArgumentException>(() => tool.InputSchema = document.RootElement);
    }

    [Theory]
    [InlineData("""{"type":"object"}""")]
    [InlineData("""{"type":"object", "properties": {}, "required" : [] }""")]
    [InlineData("""{"type":"object", "title": "MyAwesomeTool", "description": "It's awesome!", "properties": {}, "required" : ["NotAParam"] }""")]
    public static void ToolInputSchema_AcceptsValidSchemaDocuments(string validSchema)
    {
        using var document = JsonDocument.Parse(validSchema);
        Tool tool = new()
        {
            Name = "test",
            InputSchema = document.RootElement
        };

        Assert.True(JsonElement.DeepEquals(document.RootElement, tool.InputSchema));
    }
}
