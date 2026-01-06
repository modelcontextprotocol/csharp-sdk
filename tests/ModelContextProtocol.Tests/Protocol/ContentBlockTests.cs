using ModelContextProtocol.Core;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public class ContentBlockTests
{
    private static string GetUtf8String(ReadOnlyMemory<byte> bytes) =>
        McpTextUtilities.GetStringFromUtf8(bytes.Span);

    private static JsonSerializerOptions GetOptions(bool materializeUtf8TextContentBlocks) =>
        materializeUtf8TextContentBlocks
            ? McpJsonUtilities.CreateOptions(materializeUtf8TextContentBlocks: true)
            : McpJsonUtilities.DefaultOptions;

    private static string AssertTextBlock(ContentBlock contentBlock, bool materializeUtf8TextContentBlocks) =>
        materializeUtf8TextContentBlocks
            ? Assert.IsType<Utf8TextContentBlock>(contentBlock).Text
            : Assert.IsType<TextContentBlock>(contentBlock).Text;

    [Fact]
    public void ResourceLinkBlock_SerializationRoundTrip_PreservesAllProperties()
    {
        // Arrange
        var original = new ResourceLinkBlock
        {
            Uri = "https://example.com/resource",
            Name = "Test Resource",
            Description = "A test resource for validation",
            MimeType = "text/plain",
            Size = 1024
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
        Assert.Equal(original.Description, resourceLink.Description);
        Assert.Equal(original.MimeType, resourceLink.MimeType);
        Assert.Equal(original.Size, resourceLink.Size);
        Assert.Equal("resource_link", resourceLink.Type);
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
        Assert.Null(resourceLink.Description);
        Assert.Null(resourceLink.MimeType);
        Assert.Null(resourceLink.Size);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialize_IgnoresUnknownArrayProperty(bool materializeUtf8TextContentBlocks)
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

        var options = GetOptions(materializeUtf8TextContentBlocks);
        var contentBlock = JsonSerializer.Deserialize<ContentBlock>(responseJson, options);
        Assert.NotNull(contentBlock);

        Assert.Contains("1234567890", AssertTextBlock(contentBlock, materializeUtf8TextContentBlocks));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialize_IgnoresUnknownObjectProperties(bool materializeUtf8TextContentBlocks)
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

        var options = GetOptions(materializeUtf8TextContentBlocks);
        var contentBlock = JsonSerializer.Deserialize<ContentBlock>(responseJson, options);
        Assert.NotNull(contentBlock);

        Assert.Contains("Sample text", AssertTextBlock(contentBlock, materializeUtf8TextContentBlocks));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ToolResultContentBlock_WithError_SerializationRoundtrips(bool materializeUtf8TextContentBlocks)
    {
        ToolResultContentBlock toolResult = new()
        {
            ToolUseId = "call_123",
            Content = [new TextContentBlock { Text = "Error: City not found" }],
            IsError = true
        };

        var options = GetOptions(materializeUtf8TextContentBlocks);
        var json = JsonSerializer.Serialize<ContentBlock>(toolResult, options);
        var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, options);

        var result = Assert.IsType<ToolResultContentBlock>(deserialized);
        Assert.Equal("call_123", result.ToolUseId);
        Assert.True(result.IsError);
        Assert.Single(result.Content);
        Assert.Equal("Error: City not found", AssertTextBlock(result.Content[0], materializeUtf8TextContentBlocks));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ToolResultContentBlock_WithStructuredContent_SerializationRoundtrips(bool materializeUtf8TextContentBlocks)
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

        var options = GetOptions(materializeUtf8TextContentBlocks);
        var json = JsonSerializer.Serialize<ContentBlock>(toolResult, options);
        var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, options);

        var result = Assert.IsType<ToolResultContentBlock>(deserialized);
        Assert.Equal("call_123", result.ToolUseId);
        Assert.Single(result.Content);
        Assert.Equal("Result data", AssertTextBlock(result.Content[0], materializeUtf8TextContentBlocks));
        Assert.NotNull(result.StructuredContent);
        Assert.Equal(18, result.StructuredContent.Value.GetProperty("temperature").GetInt32());
        Assert.Equal("cloudy", result.StructuredContent.Value.GetProperty("condition").GetString());
        Assert.False(result.IsError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ToolResultContentBlock_SerializationRoundTrip(bool materializeUtf8TextContentBlocks)
    {
        ToolResultContentBlock toolResult = new()
        {
            ToolUseId = "call_123",
            Content =
            [
                new TextContentBlock { Text = "Result data" },
                new ImageContentBlock { Data = "base64data", MimeType = "image/png" }
            ],
            StructuredContent = JsonElement.Parse("""{"temperature":18,"condition":"cloudy"}"""),
            IsError = false
        };

        var options = GetOptions(materializeUtf8TextContentBlocks);
        var json = JsonSerializer.Serialize<ContentBlock>(toolResult, options);
        var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, options);

        var result = Assert.IsType<ToolResultContentBlock>(deserialized);
        Assert.Equal("call_123", result.ToolUseId);
        Assert.Equal(2, result.Content.Count);
        Assert.Equal("Result data", AssertTextBlock(result.Content[0], materializeUtf8TextContentBlocks));
        var imageBlock = Assert.IsType<ImageContentBlock>(result.Content[1]);
        Assert.Equal("base64data", imageBlock.Data);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Utf8TextContentBlock_SerializesAsText_AndDeserializesAsTextContentBlock(bool materializeUtf8TextContentBlocks)
    {
        // Utf8TextContentBlock is an optimization for write paths; the wire format is still a normal "text" block.
        ContentBlock original = new Utf8TextContentBlock
        {
            Utf8Text = "Sample text"u8.ToArray()
        };

        var options = GetOptions(materializeUtf8TextContentBlocks);

        string json = JsonSerializer.Serialize(original, options);
        Assert.Contains("\"type\":\"text\"", json);
        Assert.Contains("\"text\":\"Sample text\"", json);

        ContentBlock? deserialized = JsonSerializer.Deserialize<ContentBlock>(json, options);
        Assert.NotNull(deserialized);
        Assert.Equal("Sample text", AssertTextBlock(deserialized, materializeUtf8TextContentBlocks));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ToolResultContentBlock_WithUtf8TextContent_SerializationRoundtrips(bool materializeUtf8TextContentBlocks)
    {
        ToolResultContentBlock toolResult = new()
        {
            ToolUseId = "call_123",
            Content =
            [
                new Utf8TextContentBlock { Utf8Text = "Result data"u8.ToArray() }
            ],
            IsError = false
        };

        var options = GetOptions(materializeUtf8TextContentBlocks);
        var json = JsonSerializer.Serialize<ContentBlock>(toolResult, options);
        var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, options);

        var result = Assert.IsType<ToolResultContentBlock>(deserialized);
        Assert.Equal("call_123", result.ToolUseId);
        Assert.Single(result.Content);
        Assert.Equal("Result data", AssertTextBlock(result.Content[0], materializeUtf8TextContentBlocks));
        Assert.False(result.IsError);
    }
}
