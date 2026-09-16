using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class CompleteResultTests
{
    [Fact]
    public static void CompleteResult_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new CompleteResult
        {
            Completion = new Completion
            {
                Values = ["weather", "web", "webhook"],
                Total = 10,
                HasMore = true
            },
            Meta = new JsonObject { ["key"] = "value" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CompleteResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Completion);
        Assert.Equal(3, deserialized.Completion.Values.Count);
        Assert.Equal("weather", deserialized.Completion.Values[0]);
        Assert.Equal("web", deserialized.Completion.Values[1]);
        Assert.Equal("webhook", deserialized.Completion.Values[2]);
        Assert.Equal(10, deserialized.Completion.Total);
        Assert.True(deserialized.Completion.HasMore);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("value", (string)deserialized.Meta["key"]!);
    }

    [Fact]
    public static void CompleteResult_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new CompleteResult();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CompleteResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Completion);
        Assert.Empty(deserialized.Completion.Values);
        Assert.Null(deserialized.Completion.Total);
        Assert.Null(deserialized.Completion.HasMore);
        Assert.Null(deserialized.Meta);
    }
}
