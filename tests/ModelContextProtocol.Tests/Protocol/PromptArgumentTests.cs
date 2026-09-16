using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class PromptArgumentTests
{
    [Fact]
    public static void PromptArgument_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new PromptArgument
        {
            Name = "topic",
            Title = "Topic",
            Description = "The topic to discuss",
            Required = true
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<PromptArgument>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("topic", deserialized.Name);
        Assert.Equal("Topic", deserialized.Title);
        Assert.Equal("The topic to discuss", deserialized.Description);
        Assert.True(deserialized.Required);
    }

    [Fact]
    public static void PromptArgument_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new PromptArgument
        {
            Name = "input"
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<PromptArgument>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("input", deserialized.Name);
        Assert.Null(deserialized.Title);
        Assert.Null(deserialized.Description);
        Assert.Null(deserialized.Required);
    }
}
