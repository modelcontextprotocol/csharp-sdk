using Microsoft.Extensions.AI;
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
}