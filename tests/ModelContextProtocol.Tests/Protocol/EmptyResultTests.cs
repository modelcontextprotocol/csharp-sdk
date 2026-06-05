using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class EmptyResultTests
{
    [Fact]
    public static void EmptyResult_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new EmptyResult
        {
            Meta = new JsonObject { ["key"] = "value" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<EmptyResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("value", (string)deserialized.Meta["key"]!);
    }

    [Fact]
    public static void EmptyResult_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new EmptyResult();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<EmptyResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Meta);
    }
}
