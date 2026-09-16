using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class ListResourceTemplatesRequestParamsTests
{
    [Fact]
    public static void ListResourceTemplatesRequestParams_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ListResourceTemplatesRequestParams
        {
            Cursor = "cursor-xyz",
            Meta = new JsonObject { ["progressToken"] = "tok-1" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListResourceTemplatesRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("cursor-xyz", deserialized.Cursor);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("tok-1", (string)deserialized.Meta["progressToken"]!);
    }

    [Fact]
    public static void ListResourceTemplatesRequestParams_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ListResourceTemplatesRequestParams();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListResourceTemplatesRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Cursor);
        Assert.Null(deserialized.Meta);
    }
}
