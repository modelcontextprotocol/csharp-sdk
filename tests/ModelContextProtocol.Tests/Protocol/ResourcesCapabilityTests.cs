using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class ResourcesCapabilityTests
{
    [Fact]
    public static void ResourcesCapability_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ResourcesCapability
        {
            Subscribe = true,
            ListChanged = true
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ResourcesCapability>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.Subscribe);
        Assert.True(deserialized.ListChanged);
    }

    [Fact]
    public static void ResourcesCapability_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ResourcesCapability();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ResourcesCapability>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Subscribe);
        Assert.Null(deserialized.ListChanged);
    }
}
