using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class ToolAnnotationsTests
{
    [Fact]
    public static void ToolAnnotations_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ToolAnnotations
        {
            Title = "My Tool",
            DestructiveHint = true,
            IdempotentHint = false,
            OpenWorldHint = true,
            ReadOnlyHint = false
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ToolAnnotations>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("My Tool", deserialized.Title);
        Assert.True(deserialized.DestructiveHint);
        Assert.False(deserialized.IdempotentHint);
        Assert.True(deserialized.OpenWorldHint);
        Assert.False(deserialized.ReadOnlyHint);
    }

    [Fact]
    public static void ToolAnnotations_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ToolAnnotations();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ToolAnnotations>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Title);
        Assert.Null(deserialized.DestructiveHint);
        Assert.Null(deserialized.IdempotentHint);
        Assert.Null(deserialized.OpenWorldHint);
        Assert.Null(deserialized.ReadOnlyHint);
    }
}
