using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class ImplementationTests
{
    [Fact]
    public static void Implementation_SerializationRoundTrip_PreservesAllProperties()
    {
        // Arrange
        var original = new Implementation
        {
            Name = "test-server",
            Title = "Test MCP Server",
            Version = "1.0.0",
            Description = "A test MCP server implementation for demonstration purposes",
            Icons =
            [
                new() { Source = "https://example.com/icon.png", MimeType = "image/png", Sizes = ["48x48"] },
                new() { Source = "https://example.com/icon.svg", MimeType = "image/svg+xml", Sizes = ["any"] }
            ],
            WebsiteUrl = "https://example.com"
        };

        // Act - Serialize to JSON
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        
        // Act - Deserialize back from JSON  
        var deserialized = JsonSerializer.Deserialize<Implementation>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Title, deserialized.Title);
        Assert.Equal(original.Version, deserialized.Version);
        Assert.Equal(original.Description, deserialized.Description);
        Assert.Equal(original.WebsiteUrl, deserialized.WebsiteUrl);
        Assert.NotNull(deserialized.Icons);
        Assert.Equal(original.Icons.Count, deserialized.Icons.Count);
        
        for (int i = 0; i < original.Icons.Count; i++)
        {
            Assert.Equal(original.Icons[i].Source, deserialized.Icons[i].Source);
            Assert.Equal(original.Icons[i].MimeType, deserialized.Icons[i].MimeType);
            Assert.Equal(original.Icons[i].Sizes, deserialized.Icons[i].Sizes);
        }
    }

    [Fact]
    public static void Implementation_SerializationRoundTrip_WithoutOptionalProperties()
    {
        // Arrange
        var original = new Implementation
        {
            Name = "simple-server",
            Version = "1.0.0"
        };

        // Act - Serialize to JSON
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        
        // Act - Deserialize back from JSON
        var deserialized = JsonSerializer.Deserialize<Implementation>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Title, deserialized.Title);
        Assert.Equal(original.Version, deserialized.Version);
        Assert.Equal(original.Description, deserialized.Description);
        Assert.Equal(original.Icons, deserialized.Icons);
        Assert.Equal(original.WebsiteUrl, deserialized.WebsiteUrl);
    }

    [Fact]
    public static void Implementation_HasCorrectJsonPropertyNames()
    {
        var implementation = new Implementation
        {
            Name = "test-server",
            Title = "Test Server",
            Version = "1.0.0",
            Description = "Test description",
            Icons = [new() { Source = "https://example.com/icon.png" }],
            WebsiteUrl = "https://example.com"
        };

        string json = JsonSerializer.Serialize(implementation, McpJsonUtilities.DefaultOptions);

        Assert.Contains("\"name\":", json);
        Assert.Contains("\"title\":", json);
        Assert.Contains("\"version\":", json);
        Assert.Contains("\"description\":", json);
        Assert.Contains("\"icons\":", json);
        Assert.Contains("\"websiteUrl\":", json);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"title":"Test Server"}""")]
    [InlineData("""{"name":"test-server"}""")]
    [InlineData("""{"version":"1.0.0"}""")]
    [InlineData("""{"title":"Test Server","version":"1.0.0"}""")]
    [InlineData("""{"name":"test-server","title":"Test Server"}""")]
    public static void Implementation_DeserializationWithMissingRequiredProperties_ThrowsJsonException(string invalidJson)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Implementation>(invalidJson, McpJsonUtilities.DefaultOptions));
    }
}
