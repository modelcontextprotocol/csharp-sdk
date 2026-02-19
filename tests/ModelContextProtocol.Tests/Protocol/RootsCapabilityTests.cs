using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class RootsCapabilityTests
{
    [Fact]
    public static void RootsCapability_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new RootsCapability
        {
            ListChanged = true
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<RootsCapability>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.ListChanged);
    }

    [Fact]
    public static void RootsCapability_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new RootsCapability();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<RootsCapability>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.ListChanged);
    }
}
