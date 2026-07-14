using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

/// <summary>
/// Serialization tests for the request/result types introduced by the 2026-07-28 protocol revision (SEP-2575).
/// </summary>
public static class DiscoverProtocolTests
{
    [Fact]
    public static void DiscoverRequestParams_SerializationRoundTrip_WithMeta()
    {
        var original = new DiscoverRequestParams
        {
            Meta = new JsonObject
            {
                [MetaKeys.ProtocolVersion] = "2026-07-28",
                [MetaKeys.ClientInfo] = new JsonObject
                {
                    ["name"] = "test-client",
                    ["version"] = "1.0",
                },
                [MetaKeys.ClientCapabilities] = new JsonObject(),
            },
        };

        var json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<DiscoverRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("2026-07-28", (string)deserialized.Meta[MetaKeys.ProtocolVersion]!);
    }

    [Fact]
    public static void DiscoverResult_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new DiscoverResult
        {
            SupportedVersions = new List<string> { "2025-11-25", "2026-07-28" },
            Capabilities = new ServerCapabilities
            {
                Tools = new ToolsCapability { ListChanged = true },
            },
            ServerInfo = new Implementation { Name = "test-server", Version = "2.0" },
            Instructions = "Use this server for testing.",
        };

        var json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<DiscoverResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(["2025-11-25", "2026-07-28"], deserialized.SupportedVersions);
        Assert.NotNull(deserialized.Capabilities.Tools);
        Assert.True(deserialized.Capabilities.Tools.ListChanged);
        Assert.Equal("test-server", deserialized.ServerInfo.Name);
        Assert.Equal("Use this server for testing.", deserialized.Instructions);
    }

    [Fact]
    public static void DiscoverResult_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new DiscoverResult
        {
            SupportedVersions = new List<string> { "2026-07-28" },
            Capabilities = new ServerCapabilities(),
            ServerInfo = new Implementation { Name = "minimal-server", Version = "1.0" },
        };

        var json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<DiscoverResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized.SupportedVersions);
        Assert.Equal("2026-07-28", deserialized.SupportedVersions[0]);
        Assert.Null(deserialized.Instructions);
    }
}
