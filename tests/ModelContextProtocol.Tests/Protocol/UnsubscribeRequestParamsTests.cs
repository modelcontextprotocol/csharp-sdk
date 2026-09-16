using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class UnsubscribeRequestParamsTests
{
    [Fact]
    public static void UnsubscribeRequestParams_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new UnsubscribeRequestParams
        {
            Uri = "file:///home/user/data.json",
            Meta = new JsonObject { ["progressToken"] = "tok-1" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<UnsubscribeRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("file:///home/user/data.json", deserialized.Uri);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("tok-1", (string)deserialized.Meta["progressToken"]!);
    }

    [Fact]
    public static void UnsubscribeRequestParams_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new UnsubscribeRequestParams
        {
            Uri = "file:///resource"
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<UnsubscribeRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("file:///resource", deserialized.Uri);
        Assert.Null(deserialized.Meta);
    }
}
