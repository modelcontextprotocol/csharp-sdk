using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class ListToolsResultTests
{
    [Fact]
    public static void ListToolsResult_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ListToolsResult
        {
            Tools =
            [
                new Tool { Name = "get_weather", Description = "Gets weather" },
                new Tool { Name = "search", Title = "Search Tool" }
            ],
            NextCursor = "next-token",
            Meta = new JsonObject { ["key"] = "value" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListToolsResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Tools.Count);
        Assert.Equal("get_weather", deserialized.Tools[0].Name);
        Assert.Equal("Gets weather", deserialized.Tools[0].Description);
        Assert.Equal("search", deserialized.Tools[1].Name);
        Assert.Equal("Search Tool", deserialized.Tools[1].Title);
        Assert.Equal("next-token", deserialized.NextCursor);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("value", (string)deserialized.Meta["key"]!);
    }

    [Fact]
    public static void ListToolsResult_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ListToolsResult();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListToolsResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.Tools);
        Assert.Null(deserialized.NextCursor);
        Assert.Null(deserialized.Meta);
    }
}
