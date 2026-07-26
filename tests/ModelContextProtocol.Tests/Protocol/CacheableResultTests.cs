using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

/// <summary>
/// Tests for the SEP-2549 caching hints (<c>ttlMs</c> and <c>cacheScope</c>) carried by
/// <see cref="ICacheableResult"/> implementations: the results of <c>tools/list</c>,
/// <c>prompts/list</c>, <c>resources/list</c>, <c>resources/templates/list</c>, and
/// <c>resources/read</c>.
/// </summary>
public static class CacheableResultTests
{
    public static IEnumerable<object[]> CacheableResultTypes()
    {
        yield return new object[] { typeof(ListToolsResult) };
        yield return new object[] { typeof(ListPromptsResult) };
        yield return new object[] { typeof(ListResourcesResult) };
        yield return new object[] { typeof(ListResourceTemplatesResult) };
        yield return new object[] { typeof(ReadResourceResult) };
    }

    [Theory]
    [MemberData(nameof(CacheableResultTypes))]
    public static void CacheableResult_SerializesTtlMsAsIntegerMilliseconds(Type type)
    {
        var result = (ICacheableResult)Activator.CreateInstance(type)!;
        result.TimeToLive = TimeSpan.FromMilliseconds(300_000);
        result.CacheScope = CacheScope.Public;

        string json = JsonSerializer.Serialize(result, type, McpJsonUtilities.DefaultOptions);
        var node = JsonNode.Parse(json)!.AsObject();

        Assert.True(node.ContainsKey("ttlMs"));
        Assert.Equal(JsonValueKind.Number, node["ttlMs"]!.GetValueKind());
        Assert.Equal(300_000, node["ttlMs"]!.GetValue<long>());
        Assert.Equal("public", node["cacheScope"]!.GetValue<string>());

        var deserialized = (ICacheableResult)JsonSerializer.Deserialize(json, type, McpJsonUtilities.DefaultOptions)!;
        Assert.Equal(TimeSpan.FromMilliseconds(300_000), deserialized.TimeToLive);
        Assert.Equal(CacheScope.Public, deserialized.CacheScope);
    }

    [Theory]
    [MemberData(nameof(CacheableResultTypes))]
    public static void CacheableResult_PrivateScope_RoundTrips(Type type)
    {
        var result = (ICacheableResult)Activator.CreateInstance(type)!;
        result.TimeToLive = TimeSpan.Zero;
        result.CacheScope = CacheScope.Private;

        string json = JsonSerializer.Serialize(result, type, McpJsonUtilities.DefaultOptions);
        var node = JsonNode.Parse(json)!.AsObject();

        // A TTL of zero is meaningful (immediately stale) and must still be emitted.
        Assert.True(node.ContainsKey("ttlMs"));
        Assert.Equal(0, node["ttlMs"]!.GetValue<long>());
        Assert.Equal("private", node["cacheScope"]!.GetValue<string>());

        var deserialized = (ICacheableResult)JsonSerializer.Deserialize(json, type, McpJsonUtilities.DefaultOptions)!;
        Assert.Equal(TimeSpan.Zero, deserialized.TimeToLive);
        Assert.Equal(CacheScope.Private, deserialized.CacheScope);
    }

    [Theory]
    [MemberData(nameof(CacheableResultTypes))]
    public static void CacheableResult_OmitsCachingHints_WhenUnset(Type type)
    {
        object result = Activator.CreateInstance(type)!;

        string json = JsonSerializer.Serialize(result, type, McpJsonUtilities.DefaultOptions);
        var node = JsonNode.Parse(json)!.AsObject();

        // Backward compatibility: servers that do not set the hints must not emit them.
        Assert.False(node.ContainsKey("ttlMs"));
        Assert.False(node.ContainsKey("cacheScope"));

        var deserialized = (ICacheableResult)JsonSerializer.Deserialize(json, type, McpJsonUtilities.DefaultOptions)!;
        Assert.Null(deserialized.TimeToLive);
        Assert.Null(deserialized.CacheScope);
    }

    [Theory]
    [MemberData(nameof(CacheableResultTypes))]
    public static void CacheableResult_DeserializesMissingHints_AsNull(Type type)
    {
        // A response from a server that predates SEP-2549 contains neither field.
        string json = "{}";

        var deserialized = (ICacheableResult)JsonSerializer.Deserialize(json, type, McpJsonUtilities.DefaultOptions)!;
        Assert.Null(deserialized.TimeToLive);
        Assert.Null(deserialized.CacheScope);
    }

    [Theory]
    [MemberData(nameof(CacheableResultTypes))]
    public static void CacheableResult_DeserializesNegativeTtl(Type type)
    {
        // Per SEP-2549, a negative ttlMs is preserved on the DTO; callers SHOULD treat it as zero.
        string json = "{\"ttlMs\":-5,\"cacheScope\":\"public\"}";

        var deserialized = (ICacheableResult)JsonSerializer.Deserialize(json, type, McpJsonUtilities.DefaultOptions)!;
        Assert.Equal(TimeSpan.FromMilliseconds(-5), deserialized.TimeToLive);
    }

    [Theory]
    [MemberData(nameof(CacheableResultTypes))]
    public static void CacheableResult_DeserializesOversizedTtl_ClampsInsteadOfThrowing(Type type)
    {
        // A hostile or buggy server could return a ttlMs that is a valid JSON integer but exceeds the
        // range representable by TimeSpan. Deserialization must not throw (which would break reading the
        // entire list); the value is clamped to TimeSpan.MaxValue instead.
        string json = "{\"ttlMs\":9999999999999999,\"cacheScope\":\"public\"}";

        var deserialized = (ICacheableResult)JsonSerializer.Deserialize(json, type, McpJsonUtilities.DefaultOptions)!;
        Assert.Equal(TimeSpan.MaxValue, deserialized.TimeToLive);
    }

    [Theory]
    [MemberData(nameof(CacheableResultTypes))]
    public static void CacheableResult_DeserializesLargeNegativeTtl_ClampsToMinValue(Type type)
    {
        string json = "{\"ttlMs\":-9999999999999999}";

        var deserialized = (ICacheableResult)JsonSerializer.Deserialize(json, type, McpJsonUtilities.DefaultOptions)!;
        Assert.Equal(TimeSpan.MinValue, deserialized.TimeToLive);
    }

    [Theory]
    [MemberData(nameof(CacheableResultTypes))]
    public static void CacheableResult_DeserializesMaxRepresentableTtl_DoesNotThrow(Type type)
    {
        // The largest whole-millisecond count that fits in a TimeSpan must round-trip without clamping.
        long maxWholeMs = long.MaxValue / TimeSpan.TicksPerMillisecond;
        string json = $"{{\"ttlMs\":{maxWholeMs}}}";

        var deserialized = (ICacheableResult)JsonSerializer.Deserialize(json, type, McpJsonUtilities.DefaultOptions)!;
        Assert.Equal(TimeSpan.FromTicks(maxWholeMs * TimeSpan.TicksPerMillisecond), deserialized.TimeToLive);
    }

    [Theory]
    [MemberData(nameof(CacheableResultTypes))]
    public static void CacheableResult_DeserializesHugeFloatTtl_ClampsInsteadOfThrowing(Type type)
    {
        // A fractional/exponent ttlMs whose tick count overflows to +Infinity is clamped to MaxValue
        // rather than throwing. (1e400 is beyond double range, so GetDouble() itself returns +Infinity.)
        Assert.Equal(
            TimeSpan.MaxValue,
            DeserializeTtl(type, "{\"ttlMs\":1e400}"));

        // 1e308 is finite but overflows once scaled into tick-space.
        Assert.Equal(
            TimeSpan.MaxValue,
            DeserializeTtl(type, "{\"ttlMs\":1e308}"));
    }

    [Theory]
    [MemberData(nameof(CacheableResultTypes))]
    public static void CacheableResult_DeserializesNegativeInfinityFloatTtl_ClampsToMinValue(Type type)
    {
        // A large negative exponent ttlMs yields -Infinity from GetDouble(); it must clamp to MinValue,
        // not silently become long.MinValue ticks via the cast.
        Assert.Equal(
            TimeSpan.MinValue,
            DeserializeTtl(type, "{\"ttlMs\":-1e400}"));
    }

    [Theory]
    [MemberData(nameof(CacheableResultTypes))]
    public static void CacheableResult_DeserializesUnknownCacheScope_AsNull(Type type)
    {
        // A future/unknown cacheScope string must not break deserialization of the entire result; it is
        // tolerated and surfaced as null (equivalent to an absent field, which clients treat as public).
        // Non-string tokens, including objects and arrays, must likewise be tolerated and fully consumed.
        foreach (string scope in new[] { "\"shared\"", "\"\"", "123", "true", "null", "{}", "[]", "{\"a\":1}", "[1,2]" })
        {
            string json = $"{{\"cacheScope\":{scope}}}";

            var deserialized = (ICacheableResult)JsonSerializer.Deserialize(json, type, McpJsonUtilities.DefaultOptions)!;
            Assert.Null(deserialized.CacheScope);
        }
    }

    [Theory]
    [MemberData(nameof(CacheableResultTypes))]
    public static void CacheableResult_DeserializesCacheScope_CaseInsensitively(Type type)
    {
        // Casing of the security-relevant "private" hint must be honored rather than silently dropped to
        // null (which clients treat as public), so matching is case-insensitive on read.
        foreach (string scope in new[] { "PUBLIC", "Public", "pUbLiC" })
        {
            var result = (ICacheableResult)JsonSerializer.Deserialize($"{{\"cacheScope\":\"{scope}\"}}", type, McpJsonUtilities.DefaultOptions)!;
            Assert.Equal(CacheScope.Public, result.CacheScope);
        }

        foreach (string scope in new[] { "PRIVATE", "Private", "pRiVaTe" })
        {
            var result = (ICacheableResult)JsonSerializer.Deserialize($"{{\"cacheScope\":\"{scope}\"}}", type, McpJsonUtilities.DefaultOptions)!;
            Assert.Equal(CacheScope.Private, result.CacheScope);
        }
    }

    private static TimeSpan? DeserializeTtl(Type type, string json) =>
        ((ICacheableResult)JsonSerializer.Deserialize(json, type, McpJsonUtilities.DefaultOptions)!).TimeToLive;

    [Theory]
    [MemberData(nameof(CacheableResultTypes))]
    public static void CacheableResult_DeserializesFractionalTtl(Type type)
    {
        string json = "{\"ttlMs\":1.5}";

        var deserialized = (ICacheableResult)JsonSerializer.Deserialize(json, type, McpJsonUtilities.DefaultOptions)!;
        Assert.Equal(TimeSpan.FromTicks((long)(1.5 * TimeSpan.TicksPerMillisecond)), deserialized.TimeToLive);
    }

    [Fact]
    public static void CacheScope_SerializesAsLowercaseStrings()
    {
        Assert.Equal("\"public\"", JsonSerializer.Serialize(CacheScope.Public, McpJsonUtilities.DefaultOptions));
        Assert.Equal("\"private\"", JsonSerializer.Serialize(CacheScope.Private, McpJsonUtilities.DefaultOptions));
        Assert.Equal(CacheScope.Public, JsonSerializer.Deserialize<CacheScope>("\"public\"", McpJsonUtilities.DefaultOptions));
        Assert.Equal(CacheScope.Private, JsonSerializer.Deserialize<CacheScope>("\"private\"", McpJsonUtilities.DefaultOptions));
    }

    [Theory]
    [MemberData(nameof(CacheableResultTypes))]
    public static void CacheableResult_DeserializesTtlWithoutCacheScope(Type type)
    {
        // ttlMs present, cacheScope absent: the SEP says an absent scope defaults to "public",
        // but the SDK only propagates the wire value, so the DTO reports null (caller applies default).
        string json = "{\"ttlMs\":1000}";

        var deserialized = (ICacheableResult)JsonSerializer.Deserialize(json, type, McpJsonUtilities.DefaultOptions)!;
        Assert.Equal(TimeSpan.FromSeconds(1), deserialized.TimeToLive);
        Assert.Null(deserialized.CacheScope);
    }

    [Theory]
    [MemberData(nameof(CacheableResultTypes))]
    public static void CacheableResult_DeserializesCacheScopeWithoutTtl(Type type)
    {
        // cacheScope present, ttlMs absent: a server may classify cacheability without a freshness hint.
        string json = "{\"cacheScope\":\"private\"}";

        var deserialized = (ICacheableResult)JsonSerializer.Deserialize(json, type, McpJsonUtilities.DefaultOptions)!;
        Assert.Null(deserialized.TimeToLive);
        Assert.Equal(CacheScope.Private, deserialized.CacheScope);
    }

    [Theory]
    [MemberData(nameof(CacheableResultTypes))]
    public static void CacheableResult_PaginatedPages_CarryIndependentCachingHints(Type type)
    {
        // SEP-2549: each paginated page independently carries its own ttlMs/cacheScope.
        // Two result instances representing consecutive pages must round-trip distinct hints.
        var page1 = (ICacheableResult)Activator.CreateInstance(type)!;
        page1.TimeToLive = TimeSpan.FromMinutes(10);
        page1.CacheScope = CacheScope.Public;

        var page2 = (ICacheableResult)Activator.CreateInstance(type)!;
        page2.TimeToLive = TimeSpan.FromSeconds(5);
        page2.CacheScope = CacheScope.Private;

        var rt1 = (ICacheableResult)JsonSerializer.Deserialize(
            JsonSerializer.Serialize(page1, type, McpJsonUtilities.DefaultOptions), type, McpJsonUtilities.DefaultOptions)!;
        var rt2 = (ICacheableResult)JsonSerializer.Deserialize(
            JsonSerializer.Serialize(page2, type, McpJsonUtilities.DefaultOptions), type, McpJsonUtilities.DefaultOptions)!;

        Assert.Equal(TimeSpan.FromMinutes(10), rt1.TimeToLive);
        Assert.Equal(CacheScope.Public, rt1.CacheScope);
        Assert.Equal(TimeSpan.FromSeconds(5), rt2.TimeToLive);
        Assert.Equal(CacheScope.Private, rt2.CacheScope);
    }

    [Fact]
    public static void CacheableResult_MaxValueTtl_WriteThenRead_IsStableAcrossRoundTrips()
    {
        // Writing TimeSpan.MaxValue truncates the sub-millisecond remainder to a whole-millisecond
        // integer (922337203685477 ms), so the first round-trip is slightly less than MaxValue.
        // Critically, once written this value is a fixed point: further round-trips do not drift.
        var first = RoundTrip(new ListToolsResult { TimeToLive = TimeSpan.MaxValue });
        var second = RoundTrip(new ListToolsResult { TimeToLive = first.TimeToLive });

        Assert.NotEqual(TimeSpan.MaxValue, first.TimeToLive);
        Assert.Equal(first.TimeToLive, second.TimeToLive);
    }

    private static ListToolsResult RoundTrip(ListToolsResult result) =>
        JsonSerializer.Deserialize<ListToolsResult>(
            JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions), McpJsonUtilities.DefaultOptions)!;
}
