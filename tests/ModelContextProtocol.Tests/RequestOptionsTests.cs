using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Tests;

public static class RequestOptionsTests
{
    [Fact]
    public static void DefaultConstructor_AllPropertiesNull()
    {
        RequestOptions options = new();

        Assert.Null(options.Meta);
        Assert.Null(options.ProgressToken);
        Assert.Null(options.JsonSerializerOptions);
    }

    [Fact]
    public static void Meta_GetSet_RoundTrips()
    {
        RequestOptions options = new();
        JsonObject meta = new() { ["key"] = "value" };

        for (int i = 0; i < 2; i++)
        {
            options.Meta = meta;
            Assert.Same(meta, options.Meta);
            Assert.Null(options.ProgressToken);

            options.Meta = null;
            Assert.Null(options.Meta);
            Assert.Null(options.ProgressToken);
        }
    }

    [Fact]
    public static void ProgressToken_GetSet_RoundTrips()
    {
        RequestOptions options = new();
        ProgressToken token = new("my-token");

        for (int i = 0; i < 2; i++)
        {
            options.ProgressToken = token;
            Assert.Equal(token, options.ProgressToken);
            Assert.Null(options.Meta);

            options.ProgressToken = null;
            Assert.Null(options.ProgressToken);
            Assert.Null(options.Meta);
        }
    }

    [Fact]
    public static void ProgressToken_DoesNotAffectMeta()
    {
        RequestOptions options = new() { Meta = new JsonObject { ["existing"] = "data" } };

        options.ProgressToken = new ProgressToken("my-token");

        Assert.False(options.Meta.ContainsKey("progressToken"));
        Assert.Equal("data", options.Meta["existing"]?.ToString());
    }

    [Fact]
    public static void Meta_DoesNotAffectProgressToken()
    {
        RequestOptions options = new() { ProgressToken = new ProgressToken("original") };

        options.Meta = new JsonObject { ["progressToken"] = "in-meta" };

        Assert.Equal("original", options.ProgressToken?.ToString());
    }

    [Fact]
    public static void JsonSerializerOptions_GetSet_RoundTrips()
    {
        RequestOptions options = new();
        JsonSerializerOptions serializerOptions = new() { WriteIndented = true };

        for (int i = 0; i < 2; i++)
        {
            options.JsonSerializerOptions = serializerOptions;
            Assert.Same(serializerOptions, options.JsonSerializerOptions);

            options.JsonSerializerOptions = null;
            Assert.Null(options.JsonSerializerOptions);
        }
    }

    [Fact]
    public static void GetMetaForRequest_BothNull_ReturnsNull()
    {
        RequestOptions options = new();

        var actual = options.GetMetaForRequest();

        Assert.Null(actual);
    }

    [Fact]
    public static void GetMetaForRequest_OnlyMetaSet_ReturnsMeta()
    {
        JsonObject meta = new() { ["key"] = "value" };
        RequestOptions options = new() { Meta = meta };

        var actual = options.GetMetaForRequest();

        Assert.Same(meta, actual);
    }

    [Fact]
    public static void GetMetaForRequest_OnlyProgressTokenSet_ReturnsNewObjectWithToken()
    {
        RequestOptions options = new() { ProgressToken = new ProgressToken("my-token") };

        var actual = options.GetMetaForRequest();

        Assert.NotNull(actual);
        Assert.Single(actual);
        Assert.Equal("my-token", actual["progressToken"]?.ToString());

        Assert.NotSame(actual, options.Meta);
        Assert.NotSame(actual, options.GetMetaForRequest());
    }

    [Fact]
    public static void GetMetaForRequest_OnlyProgressTokenSetAsLong_ReturnsNewObjectWithToken()
    {
        RequestOptions options = new() { ProgressToken = new ProgressToken(42L) };

        var actual = options.GetMetaForRequest();

        Assert.NotNull(actual);
        Assert.Single(actual);
        Assert.Equal("42", actual["progressToken"]?.ToString());

        Assert.NotSame(actual, options.Meta);
        Assert.NotSame(actual, options.GetMetaForRequest());
    }

    [Fact]
    public static void GetMetaForRequest_BothSet_ReturnsCloneWithProgressToken()
    {
        JsonObject meta = new() { ["custom"] = "data" };
        RequestOptions options = new()
        {
            Meta = meta,
            ProgressToken = new ProgressToken("my-token")
        };

        var actual = options.GetMetaForRequest();

        Assert.NotNull(actual);
        Assert.NotSame(meta, actual);
        Assert.Equal("data", actual["custom"]?.ToString());
        Assert.Equal("my-token", actual["progressToken"]?.ToString());

        Assert.NotSame(actual, options.Meta);
        Assert.NotSame(actual, options.GetMetaForRequest());
    }

    [Fact]
    public static void GetMetaForRequest_BothSet_DoesNotModifyOriginalMeta()
    {
        JsonObject meta = new() { ["custom"] = "data" };
        RequestOptions options = new()
        {
            Meta = meta,
            ProgressToken = new ProgressToken("my-token")
        };

        _ = options.GetMetaForRequest();

        Assert.False(meta.ContainsKey("progressToken"));
        Assert.Single(meta);
    }

    [Fact]
    public static void GetMetaForRequest_MetaHasProgressToken_OverwrittenByProperty()
    {
        JsonObject meta = new()
        {
            ["custom"] = "data",
            ["progressToken"] = "meta-token"
        };
        RequestOptions options = new()
        {
            Meta = meta,
            ProgressToken = new ProgressToken("property-token")
        };

        var actual = options.GetMetaForRequest();

        Assert.Equal("property-token", actual!["progressToken"]?.ToString());
        Assert.Equal("data", actual["custom"]?.ToString());
    }

    [Fact]
    public static void GetMetaForRequest_MetaHasProgressToken_NoPropertyToken_PreservesMetaToken()
    {
        JsonObject meta = new()
        {
            ["custom"] = "data",
            ["progressToken"] = "meta-token"
        };
        RequestOptions options = new() { Meta = meta };

        var actual = options.GetMetaForRequest();

        Assert.Same(meta, actual);
        Assert.Equal("meta-token", actual!["progressToken"]?.ToString());
    }

    [Fact]
    public static void GetMetaForRequest_CalledMultipleTimes_ReturnsNewCloneEachTime()
    {
        RequestOptions options = new()
        {
            Meta = new JsonObject { ["key"] = "value" },
            ProgressToken = new ProgressToken("token")
        };

        var actual1 = options.GetMetaForRequest();
        var actual2 = options.GetMetaForRequest();

        Assert.NotSame(actual1, actual2);
        Assert.Equal(actual1!.ToJsonString(), actual2!.ToJsonString());
    }

    [Fact]
    public static void GetMetaForRequest_OnlyMeta_SameInstanceOnMultipleCalls()
    {
        JsonObject meta = new() { ["key"] = "value" };
        RequestOptions options = new() { Meta = meta };

        var actual1 = options.GetMetaForRequest();
        var actual2 = options.GetMetaForRequest();

        Assert.Same(actual1, actual2);
        Assert.Same(meta, actual1);
    }

    [Fact]
    public static void AllProperties_CanBeSetIndependently()
    {
        JsonObject meta = new() { ["field"] = "value" };
        ProgressToken token = new("independent");
        JsonSerializerOptions serializerOptions = new() { WriteIndented = true };

        RequestOptions options = new()
        {
            Meta = meta,
            ProgressToken = token,
            JsonSerializerOptions = serializerOptions
        };

        Assert.Same(meta, options.Meta);
        Assert.Equal(token, options.ProgressToken);
        Assert.Same(serializerOptions, options.JsonSerializerOptions);
    }
}