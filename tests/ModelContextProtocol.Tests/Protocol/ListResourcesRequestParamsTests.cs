using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class ListResourcesRequestParamsTests
{
    [Fact]
    public static void ListResourcesRequestParams_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ListResourcesRequestParams
        {
            Cursor = "cursor-abc",
            Meta = new JsonObject { ["progressToken"] = "tok-1" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListResourcesRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("cursor-abc", deserialized.Cursor);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("tok-1", (string)deserialized.Meta["progressToken"]!);
    }

    [Fact]
    public static void ListResourcesRequestParams_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ListResourcesRequestParams();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListResourcesRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Cursor);
        Assert.Null(deserialized.Meta);
    }
}
