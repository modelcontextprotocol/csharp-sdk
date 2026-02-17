using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class ElicitResultTests
{
    [Fact]
    public static void ElicitResult_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ElicitResult
        {
            Action = "accept",
            Content = new Dictionary<string, JsonElement>
            {
                ["name"] = JsonDocument.Parse("\"John\"").RootElement.Clone(),
                ["age"] = JsonDocument.Parse("30").RootElement.Clone()
            },
            Meta = new JsonObject { ["key"] = "value" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("accept", deserialized.Action);
        Assert.True(deserialized.IsAccepted);
        Assert.NotNull(deserialized.Content);
        Assert.Equal(2, deserialized.Content.Count);
        Assert.Equal("John", deserialized.Content["name"].GetString());
        Assert.Equal(30, deserialized.Content["age"].GetInt32());
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("value", (string)deserialized.Meta["key"]!);
    }

    [Fact]
    public static void ElicitResult_SerializationRoundTrip_WithDefaultAction()
    {
        var original = new ElicitResult();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("cancel", deserialized.Action);
        Assert.False(deserialized.IsAccepted);
        Assert.Null(deserialized.Content);
        Assert.Null(deserialized.Meta);
    }
}
