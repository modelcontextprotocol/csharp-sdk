using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class CallToolRequestParamsTests
{
    [Fact]
    public static void CallToolRequestParams_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new CallToolRequestParams
        {
            Name = "get_weather",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["city"] = JsonDocument.Parse("\"Seattle\"").RootElement.Clone(),
                ["units"] = JsonDocument.Parse("\"metric\"").RootElement.Clone()
            },
            Task = new McpTaskMetadata { TimeToLive = TimeSpan.FromHours(1) },
            Meta = new JsonObject { ["progressToken"] = "token-123" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CallToolRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.NotNull(deserialized.Arguments);
        Assert.Equal(2, deserialized.Arguments.Count);
        Assert.Equal("Seattle", deserialized.Arguments["city"].GetString());
        Assert.Equal("metric", deserialized.Arguments["units"].GetString());
        Assert.NotNull(deserialized.Task);
        Assert.Equal(original.Task.TimeToLive, deserialized.Task.TimeToLive);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("token-123", (string)deserialized.Meta["progressToken"]!);
    }

    [Fact]
    public static void CallToolRequestParams_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new CallToolRequestParams
        {
            Name = "simple_tool"
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CallToolRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Null(deserialized.Arguments);
        Assert.Null(deserialized.Task);
        Assert.Null(deserialized.Meta);
    }
}
