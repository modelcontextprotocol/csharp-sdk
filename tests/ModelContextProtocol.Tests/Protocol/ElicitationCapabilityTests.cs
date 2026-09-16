using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class ElicitationCapabilityTests
{
    [Fact]
    public static void ElicitationCapability_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ElicitationCapability
        {
            Form = new FormElicitationCapability(),
            Url = new UrlElicitationCapability()
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitationCapability>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Form);
        Assert.NotNull(deserialized.Url);
    }

    [Fact]
    public static void ElicitationCapability_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ElicitationCapability();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitationCapability>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        // The custom converter defaults Form to non-null when both Form and Url are null for backward compatibility
        Assert.NotNull(deserialized.Form);
        Assert.Null(deserialized.Url);
    }
}
