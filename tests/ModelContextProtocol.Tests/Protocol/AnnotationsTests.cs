using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class AnnotationsTests
{
    [Fact]
    public static void Annotations_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new Annotations
        {
            Audience = [Role.User, Role.Assistant],
            Priority = 0.75f,
            LastModified = new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero)
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<Annotations>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Audience);
        Assert.Equal(2, deserialized.Audience.Count);
        Assert.Equal(Role.User, deserialized.Audience[0]);
        Assert.Equal(Role.Assistant, deserialized.Audience[1]);
        Assert.Equal(original.Priority, deserialized.Priority);
        Assert.Equal(original.LastModified, deserialized.LastModified);
    }

    [Fact]
    public static void Annotations_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new Annotations();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<Annotations>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Audience);
        Assert.Null(deserialized.Priority);
        Assert.Null(deserialized.LastModified);
    }
}
