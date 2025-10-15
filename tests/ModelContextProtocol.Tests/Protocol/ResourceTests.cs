using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class ResourceTests
{
    [Fact]
    public static void Resource_SerializationRoundTrip_PreservesAllProperties()
    {
        // Arrange
        var original = new Resource
        {
            Name = "document.pdf",
            Title = "Important Document",
            Uri = "file:///path/to/document.pdf",
            Description = "An important document",
            MimeType = "application/pdf",
            Size = 1024,
            Icons =
            [
                new() { Source = "https://example.com/pdf-icon.png", MimeType = "image/png", Sizes = ["32x32"] }
            ],
            Annotations = new Annotations { Audience = [Role.User] }
        };

        // Act - Serialize to JSON
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        
        // Act - Deserialize back from JSON
        var deserialized = JsonSerializer.Deserialize<Resource>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Title, deserialized.Title);
        Assert.Equal(original.Uri, deserialized.Uri);
        Assert.Equal(original.Description, deserialized.Description);
        Assert.Equal(original.MimeType, deserialized.MimeType);
        Assert.Equal(original.Size, deserialized.Size);
        Assert.NotNull(deserialized.Icons);
        Assert.Equal(original.Icons.Count, deserialized.Icons.Count);
        Assert.Equal(original.Icons[0].Source, deserialized.Icons[0].Source);
        Assert.Equal(original.Icons[0].MimeType, deserialized.Icons[0].MimeType);
        Assert.Equal(original.Icons[0].Sizes, deserialized.Icons[0].Sizes);
        Assert.NotNull(deserialized.Annotations);
        Assert.Equal(original.Annotations.Audience, deserialized.Annotations.Audience);
    }

    [Fact]
    public static void Resource_SerializationRoundTrip_WithMinimalProperties()
    {
        // Arrange
        var original = new Resource
        {
            Name = "data.json",
            Uri = "file:///path/to/data.json"
        };

        // Act - Serialize to JSON
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        
        // Act - Deserialize back from JSON
        var deserialized = JsonSerializer.Deserialize<Resource>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Title, deserialized.Title);
        Assert.Equal(original.Uri, deserialized.Uri);
        Assert.Equal(original.Description, deserialized.Description);
        Assert.Equal(original.MimeType, deserialized.MimeType);
        Assert.Equal(original.Size, deserialized.Size);
        Assert.Equal(original.Icons, deserialized.Icons);
        Assert.Equal(original.Annotations, deserialized.Annotations);
    }

    [Fact]
    public static void Resource_HasCorrectJsonPropertyNames()
    {
        var resource = new Resource
        {
            Name = "test_resource",
            Title = "Test Resource",
            Uri = "file:///test",
            Description = "A test resource",
            MimeType = "text/plain",
            Size = 512,
            Icons = [new() { Source = "https://example.com/icon.svg" }],
            Annotations = new Annotations { Audience = [Role.User] }
        };

        string json = JsonSerializer.Serialize(resource, McpJsonUtilities.DefaultOptions);

        Assert.Contains("\"name\":", json);
        Assert.Contains("\"title\":", json);
        Assert.Contains("\"uri\":", json);
        Assert.Contains("\"description\":", json);
        Assert.Contains("\"mimeType\":", json);
        Assert.Contains("\"size\":", json);
        Assert.Contains("\"icons\":", json);
        Assert.Contains("\"annotations\":", json);
    }
}
