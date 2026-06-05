using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class ElicitRequestParamsTests
{
    [Fact]
    public static void ElicitRequestParams_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ElicitRequestParams
        {
            Mode = "form",
            ElicitationId = "elicit-123",
            Url = null,
            Message = "Please provide your details",
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties =
                {
                    ["name"] = new ElicitRequestParams.StringSchema { Description = "Your name" },
                    ["age"] = new ElicitRequestParams.NumberSchema { Description = "Your age" }
                }
            },
            Task = new McpTaskMetadata { TimeToLive = TimeSpan.FromMinutes(10) },
            Meta = new JsonObject { ["progressToken"] = "tok-1" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("form", deserialized.Mode);
        Assert.Equal("elicit-123", deserialized.ElicitationId);
        Assert.Null(deserialized.Url);
        Assert.Equal("Please provide your details", deserialized.Message);
        Assert.NotNull(deserialized.RequestedSchema);
        Assert.Equal(2, deserialized.RequestedSchema.Properties.Count);
        Assert.NotNull(deserialized.Task);
        Assert.Equal(TimeSpan.FromMinutes(10), deserialized.Task.TimeToLive);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("tok-1", (string)deserialized.Meta["progressToken"]!);
    }

    [Fact]
    public static void ElicitRequestParams_SerializationRoundTrip_UrlMode()
    {
        var original = new ElicitRequestParams
        {
            Mode = "url",
            ElicitationId = "elicit-456",
            Url = "https://example.com/auth",
            Message = "Please authenticate"
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("url", deserialized.Mode);
        Assert.Equal("elicit-456", deserialized.ElicitationId);
        Assert.Equal("https://example.com/auth", deserialized.Url);
        Assert.Equal("Please authenticate", deserialized.Message);
        Assert.Null(deserialized.RequestedSchema);
        Assert.Null(deserialized.Task);
    }

    [Fact]
    public static void ElicitRequestParams_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ElicitRequestParams
        {
            Message = "Confirm action"
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("form", deserialized.Mode);
        Assert.Equal("Confirm action", deserialized.Message);
        Assert.Null(deserialized.ElicitationId);
        Assert.Null(deserialized.Url);
        Assert.Null(deserialized.RequestedSchema);
        Assert.Null(deserialized.Task);
        Assert.Null(deserialized.Meta);
    }
}
