using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class ModelPreferencesTests
{
    [Fact]
    public static void ModelPreferences_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ModelPreferences
        {
            CostPriority = 0.3f,
            SpeedPriority = 0.7f,
            IntelligencePriority = 0.9f,
            Hints =
            [
                new ModelHint { Name = "gpt-4" },
                new ModelHint { Name = "claude-3" }
            ]
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ModelPreferences>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(0.3f, deserialized.CostPriority);
        Assert.Equal(0.7f, deserialized.SpeedPriority);
        Assert.Equal(0.9f, deserialized.IntelligencePriority);
        Assert.NotNull(deserialized.Hints);
        Assert.Equal(2, deserialized.Hints.Count);
        Assert.Equal("gpt-4", deserialized.Hints[0].Name);
        Assert.Equal("claude-3", deserialized.Hints[1].Name);
    }

    [Fact]
    public static void ModelPreferences_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ModelPreferences();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ModelPreferences>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.CostPriority);
        Assert.Null(deserialized.SpeedPriority);
        Assert.Null(deserialized.IntelligencePriority);
        Assert.Null(deserialized.Hints);
    }

    [Fact]
    public static void ModelHint_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ModelHint { Name = "gpt-4o" };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ModelHint>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("gpt-4o", deserialized.Name);
    }

    [Fact]
    public static void ModelHint_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ModelHint();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ModelHint>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Name);
    }
}
