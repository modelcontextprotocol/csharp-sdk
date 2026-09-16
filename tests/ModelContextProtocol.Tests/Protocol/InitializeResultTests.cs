using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class InitializeResultTests
{
    [Fact]
    public static void InitializeResult_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new InitializeResult
        {
            ProtocolVersion = "2024-11-05",
            Capabilities = new ServerCapabilities
            {
                Tools = new ToolsCapability { ListChanged = true },
                Logging = new LoggingCapability()
            },
            ServerInfo = new Implementation
            {
                Name = "test-server",
                Version = "2.0.0"
            },
            Instructions = "Use this server for testing",
            Meta = new JsonObject { ["key"] = "value" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<InitializeResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("2024-11-05", deserialized.ProtocolVersion);
        Assert.NotNull(deserialized.Capabilities);
        Assert.NotNull(deserialized.Capabilities.Tools);
        Assert.True(deserialized.Capabilities.Tools.ListChanged);
        Assert.NotNull(deserialized.Capabilities.Logging);
        Assert.Equal("test-server", deserialized.ServerInfo.Name);
        Assert.Equal("2.0.0", deserialized.ServerInfo.Version);
        Assert.Equal("Use this server for testing", deserialized.Instructions);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("value", (string)deserialized.Meta["key"]!);
    }

    [Fact]
    public static void InitializeResult_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new InitializeResult
        {
            ProtocolVersion = "2024-11-05",
            Capabilities = new ServerCapabilities(),
            ServerInfo = new Implementation
            {
                Name = "minimal-server",
                Version = "1.0.0"
            }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<InitializeResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("2024-11-05", deserialized.ProtocolVersion);
        Assert.NotNull(deserialized.Capabilities);
        Assert.Equal("minimal-server", deserialized.ServerInfo.Name);
        Assert.Null(deserialized.Instructions);
        Assert.Null(deserialized.Meta);
    }
}
