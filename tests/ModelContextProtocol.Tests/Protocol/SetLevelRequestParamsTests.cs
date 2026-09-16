using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class SetLevelRequestParamsTests
{
    [Fact]
    public static void SetLevelRequestParams_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new SetLevelRequestParams
        {
            Level = LoggingLevel.Debug,
            Meta = new JsonObject { ["progressToken"] = "tok-1" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<SetLevelRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(LoggingLevel.Debug, deserialized.Level);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("tok-1", (string)deserialized.Meta["progressToken"]!);
    }

    [Fact]
    public static void SetLevelRequestParams_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new SetLevelRequestParams
        {
            Level = LoggingLevel.Critical
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<SetLevelRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(LoggingLevel.Critical, deserialized.Level);
        Assert.Null(deserialized.Meta);
    }
}
