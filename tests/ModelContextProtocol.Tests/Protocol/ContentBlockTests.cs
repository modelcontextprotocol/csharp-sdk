using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public class ContentBlockTests
{
    [Fact]
    public void ResourceLinkBlock_SerializationRoundTrip_PreservesAllProperties()
    {
        // Arrange
        var original = new ResourceLinkBlock
        {
            Uri = "https://example.com/resource",
            Name = "Test Resource",
            Title = "Test Resource Display Title",
            Description = "A test resource for validation",
            MimeType = "text/plain",
            Size = 1024,
            Icons = [new Icon { Source = "https://example.com/icon.png", MimeType = "image/png" }]
        };

        // Act - Serialize to JSON
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        
        // Act - Deserialize back from JSON
        var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var resourceLink = Assert.IsType<ResourceLinkBlock>(deserialized);
        
        Assert.Equal(original.Uri, resourceLink.Uri);
        Assert.Equal(original.Name, resourceLink.Name);
        Assert.Equal(original.Title, resourceLink.Title);
        Assert.Equal(original.Description, resourceLink.Description);
        Assert.Equal(original.MimeType, resourceLink.MimeType);
        Assert.Equal(original.Size, resourceLink.Size);
        Assert.Equal("resource_link", resourceLink.Type);
        Assert.NotNull(resourceLink.Icons);
        Assert.Single(resourceLink.Icons);
        Assert.Equal("https://example.com/icon.png", resourceLink.Icons[0].Source);
        Assert.Equal("image/png", resourceLink.Icons[0].MimeType);
    }

    [Fact]
    public void ResourceLinkBlock_DeserializationWithMinimalProperties_Succeeds()
    {
        // Arrange - JSON with only required properties
        const string Json = """
            {
                "type": "resource_link",
                "uri": "https://example.com/minimal",
                "name": "Minimal Resource"
            }
            """;

        // Act
        var deserialized = JsonSerializer.Deserialize<ContentBlock>(Json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var resourceLink = Assert.IsType<ResourceLinkBlock>(deserialized);
        
        Assert.Equal("https://example.com/minimal", resourceLink.Uri);
        Assert.Equal("Minimal Resource", resourceLink.Name);
        Assert.Null(resourceLink.Title);
        Assert.Null(resourceLink.Description);
        Assert.Null(resourceLink.MimeType);
        Assert.Null(resourceLink.Size);
        Assert.Null(resourceLink.Icons);
        Assert.Equal("resource_link", resourceLink.Type);
    }

    [Fact]
    public void ResourceLinkBlock_DeserializationWithoutName_ThrowsJsonException()
    {
        // Arrange - JSON missing the required "name" property
        const string Json = """
            {
                "type": "resource_link",
                "uri": "https://example.com/missing-name"
            }
            """;

        // Act & Assert
        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ContentBlock>(Json, McpJsonUtilities.DefaultOptions));
        
        Assert.Contains("Name must be provided for 'resource_link' type", exception.Message);
    }

    [Fact]
    public void Deserialize_IgnoresUnknownArrayProperty()
    {
        // This is a regression test where a server returned an unexpected response with
        // `structuredContent` as an array nested inside a content block. This should be
        // permitted with the `structuredContent` gracefully ignored in that location.
        string responseJson = @"{
            ""type"": ""text"",
            ""text"": ""[\n  {\n    \""Data\"": \""1234567890\""\n  }\n]"",
            ""structuredContent"": [
                {
                    ""Data"": ""1234567890""
                }
            ]
        }";

        var contentBlock = JsonSerializer.Deserialize<ContentBlock>(responseJson, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(contentBlock);

        var textBlock = Assert.IsType<TextContentBlock>(contentBlock);
        Assert.Contains("1234567890", textBlock.Text);
    }

    [Fact]
    public void Deserialize_IgnoresUnknownObjectProperties()
    {
        string responseJson = @"{
            ""type"": ""text"",
            ""text"": ""Sample text"",
            ""unknownObject"": {
                ""nestedProp1"": ""value1"",
                ""nestedProp2"": {
                    ""deeplyNested"": true
                }
            }
        }";

        var contentBlock = JsonSerializer.Deserialize<ContentBlock>(responseJson, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(contentBlock);

        var textBlock = Assert.IsType<TextContentBlock>(contentBlock);
        Assert.Contains("Sample text", textBlock.Text);
    }

    [Fact]
    public void ToolResultContentBlock_WithError_SerializationRoundtrips()
    {
        ToolResultContentBlock toolResult = new()
        {
            ToolUseId = "call_123",
            Content = [new TextContentBlock { Text = "Error: City not found" }],
            IsError = true
        };

        var json = JsonSerializer.Serialize<ContentBlock>(toolResult, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions);

        var result = Assert.IsType<ToolResultContentBlock>(deserialized);
        Assert.Equal("call_123", result.ToolUseId);
        Assert.True(result.IsError);
        Assert.Single(result.Content);
        var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Equal("Error: City not found", textBlock.Text);
    }

    [Fact]
    public void ToolResultContentBlock_WithStructuredContent_SerializationRoundtrips()
    {
        ToolResultContentBlock toolResult = new()
        {
            ToolUseId = "call_123",
            Content =
            [
                new TextContentBlock { Text = "Result data" }
            ],
            StructuredContent = JsonElement.Parse("""{"temperature":18,"condition":"cloudy"}"""),
            IsError = false
        };

        var json = JsonSerializer.Serialize<ContentBlock>(toolResult, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions);

        var result = Assert.IsType<ToolResultContentBlock>(deserialized);
        Assert.Equal("call_123", result.ToolUseId);
        Assert.Single(result.Content);
        var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Equal("Result data", textBlock.Text);
        Assert.NotNull(result.StructuredContent);
        Assert.Equal(18, result.StructuredContent.Value.GetProperty("temperature").GetInt32());
        Assert.Equal("cloudy", result.StructuredContent.Value.GetProperty("condition").GetString());
        Assert.False(result.IsError);
    }

    [Fact]
    public void ToolResultContentBlock_SerializationRoundTrip()
    {
        ToolResultContentBlock toolResult = new()
        {
            ToolUseId = "call_123",
            Content =
            [
                new TextContentBlock { Text = "Result data" },
                new ImageContentBlock { Data = System.Text.Encoding.UTF8.GetBytes("base64data"), MimeType = "image/png" }
            ],
            StructuredContent = JsonElement.Parse("""{"temperature":18,"condition":"cloudy"}"""),
            IsError = false
        };

        var json = JsonSerializer.Serialize<ContentBlock>(toolResult, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions);

        var result = Assert.IsType<ToolResultContentBlock>(deserialized);
        Assert.Equal("call_123", result.ToolUseId);
        Assert.Equal(2, result.Content.Count);
        var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Equal("Result data", textBlock.Text);
        var imageBlock = Assert.IsType<ImageContentBlock>(result.Content[1]);
        Assert.Equal("base64data", System.Text.Encoding.UTF8.GetString(imageBlock.Data.ToArray()));
        Assert.Equal("image/png", imageBlock.MimeType);
        Assert.NotNull(result.StructuredContent);
        Assert.Equal(18, result.StructuredContent.Value.GetProperty("temperature").GetInt32());
        Assert.Equal("cloudy", result.StructuredContent.Value.GetProperty("condition").GetString());
        Assert.False(result.IsError);
    }

    [Fact]
    public void ToolUseContentBlock_SerializationRoundTrip()
    {
        ToolUseContentBlock toolUse = new()
        {
            Id = "call_abc123",
            Name = "get_weather",
            Input = JsonElement.Parse("""{"city":"Paris","units":"metric"}""")
        };

        var json = JsonSerializer.Serialize<ContentBlock>(toolUse, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions);

        var result = Assert.IsType<ToolUseContentBlock>(deserialized);
        Assert.Equal("call_abc123", result.Id);
        Assert.Equal("get_weather", result.Name);
        Assert.Equal("Paris", result.Input.GetProperty("city").GetString());
        Assert.Equal("metric", result.Input.GetProperty("units").GetString());
    }
}