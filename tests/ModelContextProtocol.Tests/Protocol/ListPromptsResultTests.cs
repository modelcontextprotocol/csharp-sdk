using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class ListPromptsResultTests
{
    [Fact]
    public static void ListPromptsResult_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new ListPromptsResult
        {
            Prompts =
            [
                new Prompt
                {
                    Name = "code_review",
                    Title = "Code Review",
                    Description = "Reviews code changes"
                },
                new Prompt
                {
                    Name = "summarize",
                    Description = "Summarizes text"
                }
            ],
            NextCursor = "page-2-token",
            Meta = new JsonObject { ["key"] = "value" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListPromptsResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Prompts.Count);
        Assert.Equal("code_review", deserialized.Prompts[0].Name);
        Assert.Equal("Code Review", deserialized.Prompts[0].Title);
        Assert.Equal("summarize", deserialized.Prompts[1].Name);
        Assert.Equal("page-2-token", deserialized.NextCursor);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("value", (string)deserialized.Meta["key"]!);
    }

    [Fact]
    public static void ListPromptsResult_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new ListPromptsResult();

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ListPromptsResult>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.Prompts);
        Assert.Null(deserialized.NextCursor);
        Assert.Null(deserialized.Meta);
    }
}
