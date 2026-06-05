using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class McpTaskMetadataTests
{
    [Fact]
    public static void McpTaskMetadata_SerializationRoundTrip_WithTimeToLive()
    {
        // Arrange
        var original = new McpTaskMetadata
        {
            TimeToLive = TimeSpan.FromHours(2)
        };

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<McpTaskMetadata>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.TimeToLive, deserialized.TimeToLive);
    }

    [Fact]
    public static void McpTaskMetadata_SerializationRoundTrip_WithNullTimeToLive()
    {
        // Arrange
        var original = new McpTaskMetadata();

        // Act
        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<McpTaskMetadata>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Null(deserialized.TimeToLive);
    }

    [Fact]
    public static void McpTaskMetadata_HasCorrectJsonPropertyNames()
    {
        var metadata = new McpTaskMetadata
        {
            TimeToLive = TimeSpan.FromMinutes(15)
        };

        string json = JsonSerializer.Serialize(metadata, McpJsonUtilities.DefaultOptions);

        Assert.Contains("\"ttl\":", json);
    }
}
