using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class ListRootsRequestParamsTests
{
    [Fact]
    public static void ListRootsRequestParams_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ListRootsRequestParams
        {
            Meta = new JsonObject { ["progressToken"] = "tok-1" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListRootsRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("tok-1", (string)deserialized.Meta["progressToken"]!);
    }

    [Fact]
    public static void ListRootsRequestParams_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ListRootsRequestParams();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListRootsRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Meta);
    }
}
