using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class ResourceUpdatedNotificationParamsTests
{
    [Fact]
    public static void ResourceUpdatedNotificationParams_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ResourceUpdatedNotificationParams
        {
            Uri = "file:///home/user/data.json",
            Meta = new JsonObject { ["key"] = "value" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ResourceUpdatedNotificationParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("file:///home/user/data.json", deserialized.Uri);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("value", (string)deserialized.Meta["key"]!);
    }

    [Fact]
    public static void ResourceUpdatedNotificationParams_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ResourceUpdatedNotificationParams
        {
            Uri = "file:///resource.txt"
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ResourceUpdatedNotificationParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("file:///resource.txt", deserialized.Uri);
        Assert.Null(deserialized.Meta);
    }
}
