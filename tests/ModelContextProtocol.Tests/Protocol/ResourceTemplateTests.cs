using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class ResourceTemplateTests
{
    [Fact]
    public static void ResourceTemplate_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ResourceTemplate
        {
            Name = "document",
            Title = "Document Template",
            UriTemplate = "file:///{path}",
            Description = "A file document",
            MimeType = "text/plain",
            Annotations = new Annotations
            {
                Audience = [Role.User],
                Priority = 0.8f
            },
            Icons =
            [
                new Icon { Source = "https://example.com/doc.png", MimeType = "image/png" }
            ],
            Meta = new JsonObject { ["key"] = "value" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ResourceTemplate>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("document", deserialized.Name);
        Assert.Equal("Document Template", deserialized.Title);
        Assert.Equal("file:///{path}", deserialized.UriTemplate);
        Assert.Equal("A file document", deserialized.Description);
        Assert.Equal("text/plain", deserialized.MimeType);
        Assert.NotNull(deserialized.Annotations);
        Assert.NotNull(deserialized.Annotations.Audience);
        Assert.Single(deserialized.Annotations.Audience);
        Assert.Equal(0.8f, deserialized.Annotations.Priority);
        Assert.NotNull(deserialized.Icons);
        Assert.Single(deserialized.Icons);
        Assert.Equal("https://example.com/doc.png", deserialized.Icons[0].Source);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("value", (string)deserialized.Meta["key"]!);
        Assert.True(deserialized.IsTemplated);
    }

    [Fact]
    public static void ResourceTemplate_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ResourceTemplate
        {
            Name = "static",
            UriTemplate = "file:///static"
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ResourceTemplate>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("static", deserialized.Name);
        Assert.Equal("file:///static", deserialized.UriTemplate);
        Assert.Null(deserialized.Title);
        Assert.Null(deserialized.Description);
        Assert.Null(deserialized.MimeType);
        Assert.Null(deserialized.Annotations);
        Assert.Null(deserialized.Icons);
        Assert.Null(deserialized.Meta);
        Assert.False(deserialized.IsTemplated);
    }
}
