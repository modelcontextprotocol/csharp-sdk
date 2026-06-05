using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Protocol;

public static class CompleteRequestParamsTests
{
    [Fact]
    public static void CompleteRequestParams_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new CompleteRequestParams
        {
            Ref = new PromptReference { Name = "my_prompt" },
            Argument = new Argument { Name = "topic", Value = "wea" },
            Context = new CompleteContext
            {
                Arguments = new Dictionary<string, string> { ["language"] = "en" }
            },
            Meta = new JsonObject { ["progressToken"] = "tok-1" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CompleteRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        var promptRef = Assert.IsType<PromptReference>(deserialized.Ref);
        Assert.Equal("my_prompt", promptRef.Name);
        Assert.Equal(original.Argument.Name, deserialized.Argument.Name);
        Assert.Equal(original.Argument.Value, deserialized.Argument.Value);
        Assert.NotNull(deserialized.Context);
        Assert.NotNull(deserialized.Context.Arguments);
        Assert.Equal("en", deserialized.Context.Arguments["language"]);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("tok-1", (string)deserialized.Meta["progressToken"]!);
    }

    [Fact]
    public static void CompleteRequestParams_SerializationRoundTrip_WithMinimalProperties()
    {
        var original = new CompleteRequestParams
        {
            Ref = new ResourceTemplateReference { Uri = "file:///{path}" },
            Argument = new Argument { Name = "path", Value = "/ho" }
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<CompleteRequestParams>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        var resourceRef = Assert.IsType<ResourceTemplateReference>(deserialized.Ref);
        Assert.Equal("file:///{path}", resourceRef.Uri);
        Assert.Equal("path", deserialized.Argument.Name);
        Assert.Equal("/ho", deserialized.Argument.Value);
        Assert.Null(deserialized.Context);
        Assert.Null(deserialized.Meta);
    }
}
