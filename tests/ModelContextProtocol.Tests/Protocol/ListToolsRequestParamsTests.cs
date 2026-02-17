using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class ListToolsRequestParamsTests
{
    [Fact]
    public static void ListToolsRequestParams_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ListToolsRequestParams
        {
            Cursor = "tools-cursor",
            Meta = new JsonObject { ["progressToken"] = "tok-1" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListToolsRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("tools-cursor", deserialized.Cursor);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("tok-1", (string)deserialized.Meta["progressToken"]!);
    }

    [Fact]
    public static void ListToolsRequestParams_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ListToolsRequestParams();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListToolsRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Cursor);
        Assert.Null(deserialized.Meta);
    }
}
