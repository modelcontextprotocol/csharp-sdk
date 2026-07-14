using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

/// <summary>
/// Targeted tests for the SEP-2549 caching hints (<c>ttlMs</c> and <c>cacheScope</c>) on
/// <see cref="DiscoverResult"/>. Spec PR #2855 promotes both fields to required on the discover
/// response. <see cref="DiscoverResult"/> has <c>required</c> CLR properties for
/// <see cref="DiscoverResult.SupportedVersions"/>, <see cref="DiscoverResult.Capabilities"/>, and
/// <see cref="DiscoverResult.ServerInfo"/>, which prevents reuse of the parameterized
/// <see cref="CacheableResultTests"/> helper (it instantiates via reflection). This file covers the
/// same property-shape assertions for <see cref="DiscoverResult"/>.
/// </summary>
public static class DiscoverResultCacheableTests
{
    private static DiscoverResult NewDiscoverResult() => new()
    {
        SupportedVersions = [McpProtocolVersions.November2025ProtocolVersion, McpProtocolVersions.July2026ProtocolVersion],
        Capabilities = new ServerCapabilities(),
        ServerInfo = new Implementation { Name = "test-server", Version = "1.0" },
    };

    [Fact]
    public static void DiscoverResult_SerializesTtlMsAsIntegerMilliseconds()
    {
        var result = NewDiscoverResult();
        result.TimeToLive = TimeSpan.FromMilliseconds(300_000);
        result.CacheScope = CacheScope.Public;

        string json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        var node = JsonNode.Parse(json)!.AsObject();

        Assert.True(node.ContainsKey("ttlMs"));
        Assert.Equal(JsonValueKind.Number, node["ttlMs"]!.GetValueKind());
        Assert.Equal(300_000, node["ttlMs"]!.GetValue<long>());
        Assert.Equal("public", node["cacheScope"]!.GetValue<string>());

        var deserialized = JsonSerializer.Deserialize<DiscoverResult>(json, McpJsonUtilities.DefaultOptions)!;
        Assert.Equal(TimeSpan.FromMilliseconds(300_000), deserialized.TimeToLive);
        Assert.Equal(CacheScope.Public, deserialized.CacheScope);
    }

    [Fact]
    public static void DiscoverResult_PrivateScope_RoundTrips()
    {
        var result = NewDiscoverResult();
        result.TimeToLive = TimeSpan.Zero;
        result.CacheScope = CacheScope.Private;

        string json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        var node = JsonNode.Parse(json)!.AsObject();

        Assert.True(node.ContainsKey("ttlMs"));
        Assert.Equal(0, node["ttlMs"]!.GetValue<long>());
        Assert.Equal("private", node["cacheScope"]!.GetValue<string>());

        var deserialized = JsonSerializer.Deserialize<DiscoverResult>(json, McpJsonUtilities.DefaultOptions)!;
        Assert.Equal(TimeSpan.Zero, deserialized.TimeToLive);
        Assert.Equal(CacheScope.Private, deserialized.CacheScope);
    }

    [Fact]
    public static void DiscoverResult_OmitsCachingHints_WhenUnset()
    {
        var result = NewDiscoverResult();

        string json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        var node = JsonNode.Parse(json)!.AsObject();

        // Backward compatibility: servers that do not set the hints must not emit them.
        Assert.False(node.ContainsKey("ttlMs"));
        Assert.False(node.ContainsKey("cacheScope"));

        var deserialized = JsonSerializer.Deserialize<DiscoverResult>(json, McpJsonUtilities.DefaultOptions)!;
        Assert.Null(deserialized.TimeToLive);
        Assert.Null(deserialized.CacheScope);
    }

    [Fact]
    public static void DiscoverResult_DeserializesMissingHints_AsNull()
    {
        // A response from a pre-PR-#2855 server may omit both fields. Deserialization must succeed
        // and surface them as null so callers can apply their own defaults.
        string json =
            """
            {
              "supportedVersions": ["2025-11-25"],
              "capabilities": {},
              "serverInfo": {"name": "x", "version": "1"}
            }
            """;

        var deserialized = JsonSerializer.Deserialize<DiscoverResult>(json, McpJsonUtilities.DefaultOptions)!;
        Assert.Null(deserialized.TimeToLive);
        Assert.Null(deserialized.CacheScope);
    }

    [Fact]
    public static void DiscoverResult_DeserializesUnknownCacheScope_AsNull()
    {
        // A future or unknown cacheScope string must not break deserialization of the entire result.
        string json =
            """
            {
              "supportedVersions": ["2025-11-25"],
              "capabilities": {},
              "serverInfo": {"name": "x", "version": "1"},
              "cacheScope": "shared"
            }
            """;

        var deserialized = JsonSerializer.Deserialize<DiscoverResult>(json, McpJsonUtilities.DefaultOptions)!;
        Assert.Null(deserialized.CacheScope);
    }

    [Fact]
    public static void DiscoverResult_ImplementsICacheableResult()
    {
        // Compile-time assertion that DiscoverResult participates in the shared cacheability surface
        // alongside the list/read result types.
        Assert.IsAssignableFrom<ICacheableResult>(NewDiscoverResult());
    }
}
