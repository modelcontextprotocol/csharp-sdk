using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class ToolsCapabilityTests
{
    [Fact]
    public static void ToolsCapability_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ToolsCapability
        {
            ListChanged = true
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ToolsCapability>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.ListChanged);
    }

    [Fact]
    public static void ToolsCapability_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ToolsCapability();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ToolsCapability>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.ListChanged);
    }
}
