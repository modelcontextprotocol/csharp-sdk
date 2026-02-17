using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class CapabilitiesTests
{
    [Fact]
    public static void ClientCapabilities_ExtensionsProperty_SerializationRoundTrip()
    {
        // Arrange - Use raw JSON instead of objects for source generation compatibility
        string json = """
            {
                "extensions": {
                    "io.modelcontextprotocol/oauth-client-credentials": {},
                    "io.modelcontextprotocol/test-extension": {
                        "setting1": "value1",
                        "setting2": 42
                    }
                }
            }
            """;

        // Act - Deserialize from JSON
        var deserialized = JsonSerializer.Deserialize<ClientCapabilities>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Extensions);
        Assert.Equal(2, deserialized.Extensions.Count);
        Assert.True(deserialized.Extensions.ContainsKey("io.modelcontextprotocol/oauth-client-credentials"));
        Assert.True(deserialized.Extensions.ContainsKey("io.modelcontextprotocol/test-extension"));

        // Act - Serialize back to JSON
        string roundtrippedJson = JsonSerializer.Serialize(deserialized, McpJsonUtilities.DefaultOptions);

        // Assert - Deserialize again to verify
        var deserialized2 = JsonSerializer.Deserialize<ClientCapabilities>(roundtrippedJson, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(deserialized2);
        Assert.NotNull(deserialized2.Extensions);
        Assert.Equal(2, deserialized2.Extensions.Count);
    }

    [Fact]
    public static void ClientCapabilities_ExtensionsProperty_DeserializesCorrectly()
    {
        // Arrange
        string json = """
            {
                "extensions": {
                    "io.modelcontextprotocol/test": {}
                }
            }
            """;

        // Act
        var capabilities = JsonSerializer.Deserialize<ClientCapabilities>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(capabilities);
        Assert.NotNull(capabilities.Extensions);
        Assert.Single(capabilities.Extensions);
        Assert.True(capabilities.Extensions.ContainsKey("io.modelcontextprotocol/test"));
    }

    [Fact]
    public static void ClientCapabilities_WithoutExtensions_DeserializesWithNullExtensions()
    {
        // Arrange
        string json = "{}";

        // Act
        var capabilities = JsonSerializer.Deserialize<ClientCapabilities>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(capabilities);
        Assert.Null(capabilities.Extensions);
    }

    [Fact]
    public static void ClientCapabilities_WithEmptyExtensions_DeserializesAsEmptyDictionary()
    {
        // Arrange
        string json = """
            {
                "extensions": {}
            }
            """;

        // Act
        var capabilities = JsonSerializer.Deserialize<ClientCapabilities>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(capabilities);
        Assert.NotNull(capabilities.Extensions);
        Assert.Empty(capabilities.Extensions);
    }

    [Fact]
    public static void ServerCapabilities_ExtensionsProperty_SerializationRoundTrip()
    {
        // Arrange - Use raw JSON instead of objects for source generation compatibility
        string json = """
            {
                "extensions": {
                    "io.modelcontextprotocol/apps": {},
                    "io.modelcontextprotocol/custom": {
                        "option": 42,
                        "enabled": true
                    }
                }
            }
            """;

        // Act - Deserialize from JSON
        var deserialized = JsonSerializer.Deserialize<ServerCapabilities>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Extensions);
        Assert.Equal(2, deserialized.Extensions.Count);
        Assert.True(deserialized.Extensions.ContainsKey("io.modelcontextprotocol/apps"));
        Assert.True(deserialized.Extensions.ContainsKey("io.modelcontextprotocol/custom"));

        // Act - Serialize back to JSON
        string roundtrippedJson = JsonSerializer.Serialize(deserialized, McpJsonUtilities.DefaultOptions);

        // Assert - Deserialize again to verify
        var deserialized2 = JsonSerializer.Deserialize<ServerCapabilities>(roundtrippedJson, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(deserialized2);
        Assert.NotNull(deserialized2.Extensions);
        Assert.Equal(2, deserialized2.Extensions.Count);
    }

    [Fact]
    public static void ServerCapabilities_ExtensionsProperty_DeserializesCorrectly()
    {
        // Arrange
        string json = """
            {
                "extensions": {
                    "io.modelcontextprotocol/test": {}
                }
            }
            """;

        // Act
        var capabilities = JsonSerializer.Deserialize<ServerCapabilities>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(capabilities);
        Assert.NotNull(capabilities.Extensions);
        Assert.Single(capabilities.Extensions);
        Assert.True(capabilities.Extensions.ContainsKey("io.modelcontextprotocol/test"));
    }

    [Fact]
    public static void ServerCapabilities_WithoutExtensions_DeserializesWithNullExtensions()
    {
        // Arrange
        string json = "{}";

        // Act
        var capabilities = JsonSerializer.Deserialize<ServerCapabilities>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(capabilities);
        Assert.Null(capabilities.Extensions);
    }

    [Fact]
    public static void ServerCapabilities_WithEmptyExtensions_DeserializesAsEmptyDictionary()
    {
        // Arrange
        string json = """
            {
                "extensions": {}
            }
            """;

        // Act
        var capabilities = JsonSerializer.Deserialize<ServerCapabilities>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(capabilities);
        Assert.NotNull(capabilities.Extensions);
        Assert.Empty(capabilities.Extensions);
    }

    [Fact]
    public static void ClientCapabilities_ExtensionsWithComplexValues_RoundTrips()
    {
        // Arrange
        string json = """
            {
                "extensions": {
                    "io.modelcontextprotocol/complex": {
                        "stringValue": "test",
                        "numberValue": 123,
                        "boolValue": true,
                        "arrayValue": [1, 2, 3]
                    }
                }
            }
            """;

        // Act - Deserialize from JSON
        var deserialized = JsonSerializer.Deserialize<ClientCapabilities>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Extensions);
        Assert.Single(deserialized.Extensions);
        Assert.True(deserialized.Extensions.ContainsKey("io.modelcontextprotocol/complex"));

        // Verify the complex value can be accessed as JsonElement
        var complexValue = deserialized.Extensions["io.modelcontextprotocol/complex"];
        Assert.NotNull(complexValue);

        // Act - Serialize back to JSON
        string roundtrippedJson = JsonSerializer.Serialize(deserialized, McpJsonUtilities.DefaultOptions);

        // Assert - Verify it can deserialize again
        var deserialized2 = JsonSerializer.Deserialize<ClientCapabilities>(roundtrippedJson, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(deserialized2);
        Assert.NotNull(deserialized2.Extensions);
        Assert.Single(deserialized2.Extensions);
    }

    [Fact]
    public static void ServerCapabilities_ExtensionsWithComplexValues_RoundTrips()
    {
        // Arrange
        string json = """
            {
                "extensions": {
                    "io.modelcontextprotocol/complex": {
                        "stringValue": "test",
                        "numberValue": 456,
                        "boolValue": false,
                        "arrayValue": ["a", "b", "c"]
                    }
                }
            }
            """;

        // Act - Deserialize from JSON
        var deserialized = JsonSerializer.Deserialize<ServerCapabilities>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Extensions);
        Assert.Single(deserialized.Extensions);
        Assert.True(deserialized.Extensions.ContainsKey("io.modelcontextprotocol/complex"));

        // Verify the complex value can be accessed as JsonElement
        var complexValue = deserialized.Extensions["io.modelcontextprotocol/complex"];
        Assert.NotNull(complexValue);

        // Act - Serialize back to JSON
        string roundtrippedJson = JsonSerializer.Serialize(deserialized, McpJsonUtilities.DefaultOptions);

        // Assert - Verify it can deserialize again
        var deserialized2 = JsonSerializer.Deserialize<ServerCapabilities>(roundtrippedJson, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(deserialized2);
        Assert.NotNull(deserialized2.Extensions);
        Assert.Single(deserialized2.Extensions);
    }
}
