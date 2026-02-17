using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class ElicitationCompleteNotificationParamsTests
{
    [Fact]
    public static void ElicitationCompleteNotificationParams_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ElicitationCompleteNotificationParams
        {
            ElicitationId = "elicit-abc-123",
            Meta = new JsonObject { ["key"] = "value" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitationCompleteNotificationParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("elicit-abc-123", deserialized.ElicitationId);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("value", (string)deserialized.Meta["key"]!);
    }

    [Fact]
    public static void ElicitationCompleteNotificationParams_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ElicitationCompleteNotificationParams
        {
            ElicitationId = "elicit-min"
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitationCompleteNotificationParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("elicit-min", deserialized.ElicitationId);
        Assert.Null(deserialized.Meta);
    }
}
