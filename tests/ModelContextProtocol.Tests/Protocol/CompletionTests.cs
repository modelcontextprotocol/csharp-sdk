using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class CompletionTests
{
    [Fact]
    public static void Completion_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new Completion
        {
            Values = ["option1", "option2", "option3"],
            Total = 50,
            HasMore = true
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<Completion>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(3, deserialized.Values.Count);
        Assert.Equal("option1", deserialized.Values[0]);
        Assert.Equal("option2", deserialized.Values[1]);
        Assert.Equal("option3", deserialized.Values[2]);
        Assert.Equal(50, deserialized.Total);
        Assert.True(deserialized.HasMore);
    }

    [Fact]
    public static void Completion_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new Completion();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<Completion>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.Values);
        Assert.Null(deserialized.Total);
        Assert.Null(deserialized.HasMore);
    }
}
