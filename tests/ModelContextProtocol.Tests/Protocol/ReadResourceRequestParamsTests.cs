using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class ReadResourceRequestParamsTests
{
    [Fact]
    public static void ReadResourceRequestParams_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ReadResourceRequestParams
        {
            Uri = "file:///home/user/document.txt",
            Meta = new JsonObject { ["progressToken"] = "tok-1" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ReadResourceRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("file:///home/user/document.txt", deserialized.Uri);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("tok-1", (string)deserialized.Meta["progressToken"]!);
    }

    [Fact]
    public static void ReadResourceRequestParams_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ReadResourceRequestParams
        {
            Uri = "file:///readme.md"
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ReadResourceRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("file:///readme.md", deserialized.Uri);
        Assert.Null(deserialized.Meta);
    }
}
