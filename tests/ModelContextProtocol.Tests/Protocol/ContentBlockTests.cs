using System.Text.Json;
using ModelContextProtocol.Protocol;

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
        const string json = """
            {
                "type": "resource_link",
                "uri": "https://example.com/minimal",
                "name": "Minimal Resource"
            }
            """;

        // Act
        var deserialized = JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions);

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
        const string json = """
            {
                "type": "resource_link",
                "uri": "https://example.com/missing-name"
            }
            """;

        // Act & Assert
        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions));
        
        Assert.Contains("Name must be provided for 'resource_link' type", exception.Message);
    }
}