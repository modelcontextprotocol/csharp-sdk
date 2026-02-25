using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class LoggingMessageNotificationParamsTests
{
    [Fact]
    public static void LoggingMessageNotificationParams_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new LoggingMessageNotificationParams
        {
            Level = LoggingLevel.Warning,
            Logger = "MyApp.Services",
            Data = JsonDocument.Parse("\"Something went wrong\"").RootElement.Clone(),
            Meta = new JsonObject { ["key"] = "value" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<LoggingMessageNotificationParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(LoggingLevel.Warning, deserialized.Level);
        Assert.Equal("MyApp.Services", deserialized.Logger);
        Assert.Equal("Something went wrong", deserialized.Data.GetString());
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("value", (string)deserialized.Meta["key"]!);
    }

    [Fact]
    public static void LoggingMessageNotificationParams_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new LoggingMessageNotificationParams
        {
            Level = LoggingLevel.Error,
            Data = JsonDocument.Parse("\"error occurred\"").RootElement.Clone(),
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<LoggingMessageNotificationParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(LoggingLevel.Error, deserialized.Level);
        Assert.Null(deserialized.Logger);
        Assert.Equal("error occurred", deserialized.Data.GetString());
        Assert.Null(deserialized.Meta);
    }
}
