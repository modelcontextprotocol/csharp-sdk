using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class ListPromptsRequestParamsTests
{
    [Fact]
    public static void ListPromptsRequestParams_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ListPromptsRequestParams
        {
            Cursor = "page-2-cursor",
            Meta = new JsonObject { ["progressToken"] = "tok-1" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListPromptsRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("page-2-cursor", deserialized.Cursor);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("tok-1", (string)deserialized.Meta["progressToken"]!);
    }

    [Fact]
    public static void ListPromptsRequestParams_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ListPromptsRequestParams();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListPromptsRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Cursor);
        Assert.Null(deserialized.Meta);
    }
}
