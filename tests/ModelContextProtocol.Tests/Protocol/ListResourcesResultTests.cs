using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class ListResourcesResultTests
{
    [Fact]
    public static void ListResourcesResult_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ListResourcesResult
        {
            Resources =
            [
                new Resource
                {
                    Uri = "file:///readme.md",
                    Name = "README",
                    MimeType = "text/markdown"
                }
            ],
            NextCursor = "next-page",
            Meta = new JsonObject { ["key"] = "value" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListResourcesResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized.Resources);
        Assert.Equal("file:///readme.md", deserialized.Resources[0].Uri);
        Assert.Equal("README", deserialized.Resources[0].Name);
        Assert.Equal("text/markdown", deserialized.Resources[0].MimeType);
        Assert.Equal("next-page", deserialized.NextCursor);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("value", (string)deserialized.Meta["key"]!);
    }

    [Fact]
    public static void ListResourcesResult_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ListResourcesResult();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListResourcesResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.Resources);
        Assert.Null(deserialized.NextCursor);
        Assert.Null(deserialized.Meta);
    }
}
