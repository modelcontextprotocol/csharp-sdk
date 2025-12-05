using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests;

public class AIContentExtensionsTests
{
    [Fact]
    public void CallToolResult_ToChatMessage_ProducesExpectedAIContent()
    {
        CallToolResult toolResult = new() { Content = [new TextContentBlock { Text = "This is a test message." }] };

        Assert.Throws<ArgumentNullException>(() => AIContentExtensions.ToChatMessage(null!, "call123"));
        Assert.Throws<ArgumentNullException>(() => AIContentExtensions.ToChatMessage(toolResult, null!));

        ChatMessage message = AIContentExtensions.ToChatMessage(toolResult, "call123");
        
        Assert.NotNull(message);
        Assert.Equal(ChatRole.Tool, message.Role);
        
        FunctionResultContent frc = Assert.IsType<FunctionResultContent>(Assert.Single(message.Contents));
        Assert.Same(toolResult, frc.RawRepresentation);
        Assert.Equal("call123", frc.CallId);
        JsonElement result = Assert.IsType<JsonElement>(frc.Result);
        Assert.Contains("This is a test message.", result.ToString());
    }

    [Fact]
    public void ToAIContent_ConvertsToolUseContentBlock()
    {
        Dictionary<string, object?> inputDict = new() { ["city"] = "Paris", ["units"] = "metric" };
        ToolUseContentBlock toolUse = new()
        {
            Id = "call_abc123",
            Name = "get_weather",
            Input = JsonSerializer.SerializeToElement(inputDict, McpJsonUtilities.DefaultOptions)
        };

        AIContent? aiContent = toolUse.ToAIContent();

        var functionCall = Assert.IsType<FunctionCallContent>(aiContent);
        Assert.Equal("call_abc123", functionCall.CallId);
        Assert.Equal("get_weather", functionCall.Name);
        Assert.NotNull(functionCall.Arguments);
        
        var cityArg = Assert.IsType<JsonElement>(functionCall.Arguments["city"]);
        Assert.Equal("Paris", cityArg.GetString());
        var unitsArg = Assert.IsType<JsonElement>(functionCall.Arguments["units"]);
        Assert.Equal("metric", unitsArg.GetString());
    }

    [Fact]
    public void ToAIContent_ConvertsToolResultContentBlock()
    {
        ToolResultContentBlock toolResult = new()
        {
            ToolUseId = "call_abc123",
            Content = [new TextContentBlock { Text = "Weather: 18°C" }],
            IsError = false
        };

        AIContent? aiContent = toolResult.ToAIContent();

        var functionResult = Assert.IsType<FunctionResultContent>(aiContent);
        Assert.Equal("call_abc123", functionResult.CallId);
        Assert.Null(functionResult.Exception);
        Assert.NotNull(functionResult.Result);
    }

    [Fact]
    public void ToAIContent_ConvertsToolResultContentBlockWithError()
    {
        ToolResultContentBlock toolResult = new()
        {
            ToolUseId = "call_abc123",
            Content = [new TextContentBlock { Text = "Error: Invalid city" }],
            IsError = true
        };

        AIContent? aiContent = toolResult.ToAIContent();

        var functionResult = Assert.IsType<FunctionResultContent>(aiContent);
        Assert.Equal("call_abc123", functionResult.CallId);
        Assert.NotNull(functionResult.Exception);
    }

    [Fact]
    public void ToAIContent_ConvertsToolResultWithMultipleContent()
    {
        ToolResultContentBlock toolResult = new()
        {
            ToolUseId = "call_123",
            Content =
            [
                new TextContentBlock { Text = "Text result" },
                new ImageContentBlock { Data = System.Text.Encoding.UTF8.GetBytes(Convert.ToBase64String([1, 2, 3])), MimeType = "image/png" }
            ]
        };

        AIContent? aiContent = toolResult.ToAIContent();

        var functionResult = Assert.IsType<FunctionResultContent>(aiContent);
        Assert.Equal("call_123", functionResult.CallId);
        
        var resultList = Assert.IsAssignableFrom<IList<AIContent>>(functionResult.Result);
        Assert.Equal(2, resultList.Count);
        Assert.IsType<TextContent>(resultList[0]);
        Assert.IsType<DataContent>(resultList[1]);
    }

    [Fact]
    public void ToAIContent_ToolUseToFunctionCallRoundTrip()
    {
        Dictionary<string, object?> inputDict = new() { ["param1"] = "value1", ["param2"] = 42 };
        ToolUseContentBlock original = new()
        {
            Id = "call_123",
            Name = "test_tool",
            Input = JsonSerializer.SerializeToElement(inputDict, McpJsonUtilities.DefaultOptions)
        };

        var functionCall = Assert.IsType<FunctionCallContent>(original.ToAIContent());

        Assert.Equal("call_123", functionCall.CallId);
        Assert.Equal("test_tool", functionCall.Name);
        Assert.NotNull(functionCall.Arguments);
        
        var param1 = Assert.IsType<JsonElement>(functionCall.Arguments["param1"]);
        Assert.Equal("value1", param1.GetString());
        var param2 = Assert.IsType<JsonElement>(functionCall.Arguments["param2"]);
        Assert.Equal(42, param2.GetInt32());
    }

    [Fact]
    public void ToAIContent_ToolResultToFunctionResultRoundTrip()
    {
        ToolResultContentBlock original = new()
        {
            ToolUseId = "call_123",
            Content = [new TextContentBlock { Text = "Result" }, new TextContentBlock { Text = "More data" }],
            IsError = false
        };

        var functionResult = Assert.IsType<FunctionResultContent>(original.ToAIContent());

        Assert.Equal("call_123", functionResult.CallId);
        Assert.False(functionResult.Exception != null);
        Assert.NotNull(functionResult.Result);
    }
}