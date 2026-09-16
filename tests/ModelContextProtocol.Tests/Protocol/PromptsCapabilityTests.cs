using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class PromptsCapabilityTests
{
    [Fact]
    public static void PromptsCapability_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new PromptsCapability
        {
            ListChanged = true
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<PromptsCapability>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.ListChanged);
    }

    [Fact]
    public static void PromptsCapability_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new PromptsCapability();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<PromptsCapability>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.ListChanged);
    }
}
