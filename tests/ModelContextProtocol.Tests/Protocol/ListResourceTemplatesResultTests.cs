using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class ListResourceTemplatesResultTests
{
    [Fact]
    public static void ListResourceTemplatesResult_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ListResourceTemplatesResult
        {
            ResourceTemplates =
            [
                new ResourceTemplate
                {
                    Name = "document",
                    UriTemplate = "file:///{path}",
                    Description = "A document",
                    MimeType = "text/plain"
                }
            ],
            NextCursor = "cursor-abc",
            Meta = new JsonObject { ["key"] = "value" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListResourceTemplatesResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized.ResourceTemplates);
        Assert.Equal("document", deserialized.ResourceTemplates[0].Name);
        Assert.Equal("file:///{path}", deserialized.ResourceTemplates[0].UriTemplate);
        Assert.Equal("A document", deserialized.ResourceTemplates[0].Description);
        Assert.Equal("text/plain", deserialized.ResourceTemplates[0].MimeType);
        Assert.Equal("cursor-abc", deserialized.NextCursor);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("value", (string)deserialized.Meta["key"]!);
    }

    [Fact]
    public static void ListResourceTemplatesResult_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ListResourceTemplatesResult();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListResourceTemplatesResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.ResourceTemplates);
        Assert.Null(deserialized.NextCursor);
        Assert.Null(deserialized.Meta);
    }
}
