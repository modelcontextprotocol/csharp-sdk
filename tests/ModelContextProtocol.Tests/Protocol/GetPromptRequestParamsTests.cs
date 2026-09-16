using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class GetPromptRequestParamsTests
{
    [Fact]
    public static void GetPromptRequestParams_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new GetPromptRequestParams
        {
            Name = "code_review",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["language"] = JsonDocument.Parse("\"csharp\"").RootElement.Clone()
            },
            Meta = new JsonObject { ["progressToken"] = "tok-1" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<GetPromptRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("code_review", deserialized.Name);
        Assert.NotNull(deserialized.Arguments);
        Assert.Equal("csharp", deserialized.Arguments["language"].GetString());
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("tok-1", (string)deserialized.Meta["progressToken"]!);
    }

    [Fact]
    public static void GetPromptRequestParams_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new GetPromptRequestParams
        {
            Name = "simple_prompt"
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<GetPromptRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("simple_prompt", deserialized.Name);
        Assert.Null(deserialized.Arguments);
        Assert.Null(deserialized.Meta);
    }
}
