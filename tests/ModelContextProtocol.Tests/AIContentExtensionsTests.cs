using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Serialization;

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
                new ImageContentBlock { Data = Convert.ToBase64String([1, 2, 3]), MimeType = "image/png" }
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

    // Tests for anonymous types in AdditionalProperties (sampling pipeline regression fix)
    // These tests require reflection-based serialization and will be skipped when reflection is disabled.

    [Fact]
    public void ToContentBlock_WithAnonymousTypeInAdditionalProperties_DoesNotThrow()
    {
        if (!JsonSerializer.IsReflectionEnabledByDefault)
        {
            return;
        }

        // This is the minimal repro from the issue
        AIContent c = new()
        {
            AdditionalProperties = new()
            {
                ["data"] = new { X = 1.0, Y = 2.0 }
            }
        };

        // Should not throw NotSupportedException
        var contentBlock = c.ToContentBlock();

        Assert.NotNull(contentBlock);
        Assert.NotNull(contentBlock.Meta);
        Assert.True(contentBlock.Meta.ContainsKey("data"));
    }

    [Fact]
    public void ToContentBlock_WithMultipleAnonymousTypes_DoesNotThrow()
    {
        if (!JsonSerializer.IsReflectionEnabledByDefault)
        {
            return;
        }

        AIContent c = new()
        {
            AdditionalProperties = new()
            {
                ["point"] = new { X = 1.0, Y = 2.0 },
                ["metadata"] = new { Name = "Test", Id = 42 },
                ["config"] = new { Enabled = true, Timeout = 30 }
            }
        };

        var contentBlock = c.ToContentBlock();

        Assert.NotNull(contentBlock);
        Assert.NotNull(contentBlock.Meta);
        Assert.Equal(3, contentBlock.Meta.Count);
    }

    [Fact]
    public void ToContentBlock_WithNestedAnonymousTypes_DoesNotThrow()
    {
        if (!JsonSerializer.IsReflectionEnabledByDefault)
        {
            return;
        }

        AIContent c = new()
        {
            AdditionalProperties = new()
            {
                ["outer"] = new 
                { 
                    Inner = new { Value = "test" },
                    Count = 5
                }
            }
        };

        var contentBlock = c.ToContentBlock();

        Assert.NotNull(contentBlock);
        Assert.NotNull(contentBlock.Meta);
        Assert.True(contentBlock.Meta.ContainsKey("outer"));
    }

    [Fact]
    public void ToContentBlock_WithMixedTypesInAdditionalProperties_DoesNotThrow()
    {
        if (!JsonSerializer.IsReflectionEnabledByDefault)
        {
            return;
        }

        AIContent c = new()
        {
            AdditionalProperties = new()
            {
                ["anonymous"] = new { X = 1.0, Y = 2.0 },
                ["string"] = "test",
                ["number"] = 42,
                ["boolean"] = true,
                ["array"] = new[] { 1, 2, 3 }
            }
        };

        var contentBlock = c.ToContentBlock();

        Assert.NotNull(contentBlock);
        Assert.NotNull(contentBlock.Meta);
        Assert.Equal(5, contentBlock.Meta.Count);
    }

    [Fact]
    public void TextContent_ToContentBlock_WithAnonymousTypeInAdditionalProperties_PreservesData()
    {
        if (!JsonSerializer.IsReflectionEnabledByDefault)
        {
            return;
        }

        TextContent textContent = new("Hello, world!")
        {
            AdditionalProperties = new()
            {
                ["location"] = new { Lat = 40.7128, Lon = -74.0060 }
            }
        };

        var contentBlock = textContent.ToContentBlock();
        var textBlock = Assert.IsType<TextContentBlock>(contentBlock);

        Assert.Equal("Hello, world!", textBlock.Text);
        Assert.NotNull(textBlock.Meta);
        Assert.True(textBlock.Meta.ContainsKey("location"));
    }

    [Fact]
    public void DataContent_ToContentBlock_WithAnonymousTypeInAdditionalProperties_PreservesData()
    {
        if (!JsonSerializer.IsReflectionEnabledByDefault)
        {
            return;
        }

        byte[] imageData = [1, 2, 3, 4, 5];
        DataContent dataContent = new(imageData, "image/png")
        {
            AdditionalProperties = new()
            {
                ["dimensions"] = new { Width = 100, Height = 200 }
            }
        };

        var contentBlock = dataContent.ToContentBlock();
        var imageBlock = Assert.IsType<ImageContentBlock>(contentBlock);

        Assert.Equal(Convert.ToBase64String(imageData), imageBlock.Data);
        Assert.Equal("image/png", imageBlock.MimeType);
        Assert.NotNull(imageBlock.Meta);
        Assert.True(imageBlock.Meta.ContainsKey("dimensions"));
    }

    [Fact]
    public void ToContentBlock_WithCustomSerializerOptions_UsesProvidedOptions()
    {
        if (!JsonSerializer.IsReflectionEnabledByDefault)
        {
            return;
        }

        // Create custom options with specific settings
        var customOptions = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        AIContent c = new()
        {
            AdditionalProperties = new()
            {
                ["TestData"] = new { MyProperty = "value" }
            }
        };

        var contentBlock = c.ToContentBlock(customOptions);

        Assert.NotNull(contentBlock);
        Assert.NotNull(contentBlock.Meta);
        
        // Verify that the custom naming policy was applied
        var json = contentBlock.Meta.ToString();
        Assert.Contains("my_property", json.ToLowerInvariant());
    }

    [Fact]
    public void ToContentBlock_WithNamedUserDefinedTypeInAdditionalProperties_Works()
    {
        // This test should work regardless of reflection being enabled/disabled
        // because named types can be handled by source generators

        // Create options with source generation support for the test type
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        options.TypeInfoResolverChain.Add(NamedTypeTestJsonContext.Default);
        
        // Define a simple named type
        var testData = new TestCoordinates { X = 1.0, Y = 2.0 };
        
        AIContent c = new()
        {
            AdditionalProperties = new()
            {
                ["coordinates"] = testData
            }
        };

        // Should not throw NotSupportedException
        var contentBlock = c.ToContentBlock(options);

        Assert.NotNull(contentBlock);
        Assert.NotNull(contentBlock.Meta);
        Assert.True(contentBlock.Meta.ContainsKey("coordinates"));
        
        // Verify the data was serialized correctly
        var coordinatesNode = contentBlock.Meta["coordinates"];
        Assert.NotNull(coordinatesNode);
        
        var json = coordinatesNode.ToString();
        Assert.Contains("1", json);
        Assert.Contains("2", json);
    }

    [Fact]
    public void ToChatMessage_CallToolResult_WithAnonymousTypeInContent_Works()
    {
        if (!JsonSerializer.IsReflectionEnabledByDefault)
        {
            return;
        }

        // Create a CallToolResult with anonymous type data in the content
        var result = new CallToolResult
        {
            Content = new List<ContentBlock>
            {
                new TextContentBlock 
                { 
                    Text = "Result with metadata",
                    Meta = JsonSerializer.SerializeToNode(new { Status = "success", Code = 200 }) as System.Text.Json.Nodes.JsonObject
                }
            }
        };

        // This should not throw NotSupportedException
        var exception = Record.Exception(() => result.ToChatMessage("call_123"));
        
        Assert.Null(exception);
    }

    /// <summary>
    /// Regression test for approval request/response correlation bug.
    /// This test ensures that approval request messages are stored in the dictionary
    /// keyed by FunctionCall.CallId (not by FunctionApprovalRequestContent.Id),
    /// so that the original request message metadata is properly retained when
    /// reconstructing function call messages from approved requests.
    /// </summary>
    [Fact]
#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates
    public void ProcessFunctionApprovalResponses_MaintainsMessageMetadata()
    {
        // Arrange: Create a message history with approval requests and responses
        const string messageId = "msg_123";
        const string callId = "call_abc";
        const string authorName = "TestUser";
        
        var functionCall = new FunctionCallContent(callId, "test_function", new Dictionary<string, object?> { ["param"] = "value" });
        var approvalRequest = new FunctionApprovalRequestContent("req_456", functionCall);
        
        var requestMessage = new ChatMessage(ChatRole.Assistant, [approvalRequest])
        {
            MessageId = messageId,
            AuthorName = authorName,
            AdditionalProperties = new() { ["custom_prop"] = "test_value" }
        };
        
        var approvalResponse = new FunctionApprovalResponseContent(callId, approved: true, functionCall);
        var responseMessage = new ChatMessage(ChatRole.User, [approvalResponse]);
        
        List<ChatMessage> messages = [requestMessage, responseMessage];
        
        // Act: Process the messages
        var (processedMessages, approvalResponses) = messages.ProcessFunctionApprovalResponses();
        
        // Assert: Verify that approval responses were extracted
        Assert.Single(approvalResponses);
        Assert.Same(approvalResponse, approvalResponses[0]);
        
        // Assert: Verify the reconstructed function call message exists
        var reconstructedMessage = processedMessages.FirstOrDefault(m => 
            m.Role == ChatRole.Assistant && 
            m.Contents.Any(c => c is FunctionCallContent));
        Assert.NotNull(reconstructedMessage);
        
        // Assert: Verify that the original message metadata is retained
        // This is the key assertion - without the fix, MessageId would be null or incorrect
        // because the dictionary lookup by CallId would fail (since it was keyed by farc.Id)
        Assert.Equal(messageId, reconstructedMessage.MessageId);
        Assert.Equal(authorName, reconstructedMessage.AuthorName);
        Assert.NotNull(reconstructedMessage.AdditionalProperties);
        Assert.Equal("test_value", reconstructedMessage.AdditionalProperties["custom_prop"]);
        
        // Assert: Verify the function call content is correct
        var reconstructedCall = Assert.IsType<FunctionCallContent>(Assert.Single(reconstructedMessage.Contents));
        Assert.Equal(callId, reconstructedCall.CallId);
        Assert.Equal("test_function", reconstructedCall.Name);
    }
    
    /// <summary>
    /// Test that verifies approval requests and responses are removed from the message list
    /// after processing.
    /// </summary>
    [Fact]
    public void ProcessFunctionApprovalResponses_RemovesApprovalContent()
    {
        // Arrange
        var functionCall = new FunctionCallContent("call_1", "fn1", new Dictionary<string, object?>());
        var approvalRequest = new FunctionApprovalRequestContent("req_1", functionCall);
        var approvalResponse = new FunctionApprovalResponseContent("call_1", approved: true, functionCall);
        
        var textContent = new TextContent("Regular text");
        
        List<ChatMessage> messages =
        [
            new ChatMessage(ChatRole.User, [textContent]),
            new ChatMessage(ChatRole.Assistant, [approvalRequest]),
            new ChatMessage(ChatRole.User, [approvalResponse]),
        ];
        
        // Act
        var (processedMessages, _) = messages.ProcessFunctionApprovalResponses();
        
        // Assert: Original text message should remain
        Assert.Contains(processedMessages, m => m.Contents.Any(c => c is TextContent));
        
        // Assert: Approval request and response messages should be removed or empty
        Assert.DoesNotContain(processedMessages, m => m.Contents.Any(c => c is FunctionApprovalRequestContent));
        Assert.DoesNotContain(processedMessages, m => m.Contents.Any(c => c is FunctionApprovalResponseContent));
        
        // Assert: A new reconstructed function call message should be added
        Assert.Contains(processedMessages, m => m.Contents.Any(c => c is FunctionCallContent));
    }
    
    /// <summary>
    /// Test that verifies rejected approvals do not result in reconstructed function calls.
    /// </summary>
    [Fact]
    public void ProcessFunctionApprovalResponses_DoesNotReconstructRejectedCalls()
    {
        // Arrange
        var functionCall = new FunctionCallContent("call_1", "fn1", new Dictionary<string, object?>());
        var approvalRequest = new FunctionApprovalRequestContent("req_1", functionCall);
        var approvalResponse = new FunctionApprovalResponseContent("call_1", approved: false, functionCall);
        
        List<ChatMessage> messages =
        [
            new ChatMessage(ChatRole.Assistant, [approvalRequest]),
            new ChatMessage(ChatRole.User, [approvalResponse]),
        ];
        
        // Act
        var (processedMessages, approvalResponses) = messages.ProcessFunctionApprovalResponses();
        
        // Assert: Approval response was extracted
        Assert.Single(approvalResponses);
        Assert.False(approvalResponses[0].Approved);
        
        // Assert: No reconstructed function call message (because it was rejected)
        Assert.DoesNotContain(processedMessages, m => m.Contents.Any(c => c is FunctionCallContent));
    }
    
    /// <summary>
    /// Test that verifies parallel/multiple approval requests are handled correctly.
    /// </summary>
    [Fact]
    public void ProcessFunctionApprovalResponses_HandlesMultipleApprovals()
    {
        // Arrange
        const string messageId = "msg_multi";
        
        var call1 = new FunctionCallContent("call_1", "fn1", new Dictionary<string, object?>());
        var call2 = new FunctionCallContent("call_2", "fn2", new Dictionary<string, object?>());
        
        var request1 = new FunctionApprovalRequestContent("req_1", call1);
        var request2 = new FunctionApprovalRequestContent("req_2", call2);
        
        var requestMessage = new ChatMessage(ChatRole.Assistant, [request1, request2])
        {
            MessageId = messageId,
        };
        
        var response1 = new FunctionApprovalResponseContent("call_1", approved: true, call1);
        var response2 = new FunctionApprovalResponseContent("call_2", approved: true, call2);
        
        var responseMessage = new ChatMessage(ChatRole.User, [response1, response2]);
        
        List<ChatMessage> messages = [requestMessage, responseMessage];
        
        // Act
        var (processedMessages, approvalResponses) = messages.ProcessFunctionApprovalResponses();
        
        // Assert: Both approval responses were extracted
        Assert.Equal(2, approvalResponses.Count);
        
        // Assert: Reconstructed message contains both function calls
        var reconstructedMessage = processedMessages.FirstOrDefault(m => 
            m.Role == ChatRole.Assistant && 
            m.Contents.Any(c => c is FunctionCallContent));
        Assert.NotNull(reconstructedMessage);
        Assert.Equal(messageId, reconstructedMessage.MessageId);
        
        var functionCalls = reconstructedMessage.Contents.OfType<FunctionCallContent>().ToList();
        Assert.Equal(2, functionCalls.Count);
        Assert.Contains(functionCalls, fc => fc.CallId == "call_1");
        Assert.Contains(functionCalls, fc => fc.CallId == "call_2");
    }
#pragma warning restore MEAI001
}

// Test type for named user-defined type test
internal record TestCoordinates
{
    public double X { get; init; }
    public double Y { get; init; }
}

// Source generation context for the test type
[JsonSerializable(typeof(TestCoordinates))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, object>))]
internal partial class NamedTypeTestJsonContext : JsonSerializerContext;