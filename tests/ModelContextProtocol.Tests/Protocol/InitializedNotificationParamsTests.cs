using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class InitializedNotificationParamsTests
{
    [Fact]
    public static void InitializedNotificationParams_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new InitializedNotificationParams
        {
            Meta = new JsonObject { ["key"] = "value" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<InitializedNotificationParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("value", (string)deserialized.Meta["key"]!);
    }

    [Fact]
    public static void InitializedNotificationParams_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new InitializedNotificationParams();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<InitializedNotificationParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Meta);
    }
}
