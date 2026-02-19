using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class CancelledNotificationParamsTests
{
    [Fact]
    public static void CancelledNotificationParams_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new CancelledNotificationParams
        {
            RequestId = new RequestId(42),
            Reason = "User cancelled the operation",
            Meta = new JsonObject { ["key"] = "value" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CancelledNotificationParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(original.RequestId, deserialized.RequestId);
        Assert.Equal(original.Reason, deserialized.Reason);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("value", (string)deserialized.Meta["key"]!);
    }

    [Fact]
    public static void CancelledNotificationParams_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new CancelledNotificationParams
        {
            RequestId = new RequestId("req-123")
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CancelledNotificationParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(original.RequestId, deserialized.RequestId);
        Assert.Null(deserialized.Reason);
        Assert.Null(deserialized.Meta);
    }
}
