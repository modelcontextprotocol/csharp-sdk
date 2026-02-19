using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class CompleteContextTests
{
    [Fact]
    public static void CompleteContext_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new CompleteContext
        {
            Arguments = new Dictionary<string, string>
            {
                ["language"] = "en",
                ["region"] = "us"
            }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CompleteContext>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Arguments);
        Assert.Equal(2, deserialized.Arguments.Count);
        Assert.Equal("en", deserialized.Arguments["language"]);
        Assert.Equal("us", deserialized.Arguments["region"]);
    }

    [Fact]
    public static void CompleteContext_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new CompleteContext();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CompleteContext>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Arguments);
    }
}
