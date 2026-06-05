using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class RootTests
{
    [Fact]
    public static void Root_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new Root
        {
            Uri = "file:///home/user/project",
            Name = "My Project",
            Meta = new JsonObject { ["custom"] = "data" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<Root>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("file:///home/user/project", deserialized.Uri);
        Assert.Equal("My Project", deserialized.Name);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("data", (string?)deserialized.Meta["custom"]);
    }

    [Fact]
    public static void Root_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new Root
        {
            Uri = "file:///tmp"
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<Root>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("file:///tmp", deserialized.Uri);
        Assert.Null(deserialized.Name);
        Assert.Null(deserialized.Meta);
    }
}
