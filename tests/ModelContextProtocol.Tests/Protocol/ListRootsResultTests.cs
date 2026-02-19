using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class ListRootsResultTests
{
    [Fact]
    public static void ListRootsResult_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ListRootsResult
        {
            Roots =
            [
                new Root { Uri = "file:///home/user/project", Name = "Project" },
                new Root { Uri = "file:///home/user/docs", Name = "Docs" }
            ],
            Meta = new JsonObject { ["key"] = "value" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListRootsResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Roots.Count);
        Assert.Equal("file:///home/user/project", deserialized.Roots[0].Uri);
        Assert.Equal("Project", deserialized.Roots[0].Name);
        Assert.Equal("file:///home/user/docs", deserialized.Roots[1].Uri);
        Assert.Equal("Docs", deserialized.Roots[1].Name);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("value", (string)deserialized.Meta["key"]!);
    }
}
