using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class SamplingCapabilityTests
{
    [Fact]
    public static void SamplingCapability_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new SamplingCapability
        {
            Context = new SamplingContextCapability(),
            Tools = new SamplingToolsCapability()
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<SamplingCapability>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Context);
        Assert.NotNull(deserialized.Tools);
    }

    [Fact]
    public static void SamplingCapability_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new SamplingCapability();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<SamplingCapability>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Context);
        Assert.Null(deserialized.Tools);
    }
}
