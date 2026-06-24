using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

/// <summary>
/// Serialization tests for the subscriptions/listen types introduced by the 2026-07-28 protocol revision (SEP-2575).
/// </summary>
public static class SubscriptionsListenProtocolTests
{
    [Fact]
    public static void SubscriptionsListenRequestParams_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new SubscriptionsListenRequestParams
        {
            Notifications = new SubscriptionsListenNotifications
            {
                ToolsListChanged = true,
                PromptsListChanged = true,
                ResourcesListChanged = true,
                ResourceSubscriptions = new List<string> { "file:///foo.txt", "file:///bar.txt" },
            },
            Meta = new JsonObject
            {
                [MetaKeys.ProtocolVersion] = "2026-07-28",
                [MetaKeys.LogLevel] = "info",
            },
        };

        var json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<SubscriptionsListenRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.Notifications.ToolsListChanged);
        Assert.True(deserialized.Notifications.PromptsListChanged);
        Assert.True(deserialized.Notifications.ResourcesListChanged);
        Assert.NotNull(deserialized.Notifications.ResourceSubscriptions);
        Assert.Equal(["file:///foo.txt", "file:///bar.txt"], deserialized.Notifications.ResourceSubscriptions);
        Assert.Equal("2026-07-28", (string)deserialized.Meta![MetaKeys.ProtocolVersion]!);
    }

    [Fact]
    public static void SubscriptionsAcknowledgedNotificationParams_SerializationRoundTrip_PreservesNotifications()
    {
        var original = new SubscriptionsAcknowledgedNotificationParams
        {
            Notifications = new SubscriptionsListenNotifications
            {
                ToolsListChanged = true,
            },
        };

        var json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<SubscriptionsAcknowledgedNotificationParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.Notifications.ToolsListChanged);
        Assert.Null(deserialized.Notifications.PromptsListChanged);
        Assert.Null(deserialized.Notifications.ResourcesListChanged);
        Assert.Null(deserialized.Notifications.ResourceSubscriptions);
    }
}
