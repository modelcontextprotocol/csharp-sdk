using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Tests.Protocol;

/// <summary>
/// Tests that custom JSON converters properly handle unknown additional properties
/// for forward compatibility with future protocol versions.
/// </summary>
public class UnknownPropertiesTests
{
    [Fact]
    public void ContentBlock_DeserializationWithUnknownProperty_SkipsProperty()
    {
        // Arrange - JSON with unknown "unknownField" property
        const string Json = """
            {
                "type": "text",
                "text": "Hello, world!",
                "unknownField": "should be skipped"
            }
            """;

        // Act
        var deserialized = JsonSerializer.Deserialize<ContentBlock>(Json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var textBlock = Assert.IsType<TextContentBlock>(deserialized);
        Assert.Equal("Hello, world!", textBlock.Text);
    }

    [Fact]
    public void ContentBlock_DeserializationWithStructuredContentInContent_SkipsProperty()
    {
        // Arrange - This was the actual bug case: structuredContent incorrectly placed
        // inside a ContentBlock instead of at CallToolResult level
        const string Json = """
            {
                "type": "text",
                "text": "Result text",
                "structuredContent": {
                    "data": "this should be ignored"
                }
            }
            """;

        // Act - Should not throw
        var deserialized = JsonSerializer.Deserialize<ContentBlock>(Json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var textBlock = Assert.IsType<TextContentBlock>(deserialized);
        Assert.Equal("Result text", textBlock.Text);
    }

    [Fact]
    public void ContentBlock_DeserializationWithMultipleUnknownProperties_SkipsAll()
    {
        // Arrange - JSON with multiple unknown properties
        const string Json = """
            {
                "type": "image",
                "data": "base64data",
                "mimeType": "image/png",
                "futureProperty1": "value1",
                "futureProperty2": {"nested": "object"},
                "futureProperty3": [1, 2, 3]
            }
            """;

        // Act
        var deserialized = JsonSerializer.Deserialize<ContentBlock>(Json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var imageBlock = Assert.IsType<ImageContentBlock>(deserialized);
        Assert.Equal("base64data", System.Text.Encoding.UTF8.GetString(imageBlock.Data.ToArray()));
        Assert.Equal("image/png", imageBlock.MimeType);
    }

    [Fact]
    public void Reference_DeserializationWithUnknownProperty_SkipsProperty()
    {
        // Arrange - JSON with unknown "metadata" property
        const string Json = """
            {
                "type": "ref/prompt",
                "name": "test-prompt",
                "metadata": {
                    "version": "2.0",
                    "author": "future"
                }
            }
            """;

        // Act
        var deserialized = JsonSerializer.Deserialize<Reference>(Json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var promptRef = Assert.IsType<PromptReference>(deserialized);
        Assert.Equal("test-prompt", promptRef.Name);
    }

    [Fact]
    public void Reference_DeserializationWithMultipleUnknownProperties_SkipsAll()
    {
        // Arrange
        const string Json = """
            {
                "type": "ref/resource",
                "uri": "file:///test.txt",
                "extraField1": "ignored",
                "extraField2": 123,
                "extraField3": true
            }
            """;

        // Act
        var deserialized = JsonSerializer.Deserialize<Reference>(Json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var resourceRef = Assert.IsType<ResourceTemplateReference>(deserialized);
        Assert.Equal("file:///test.txt", resourceRef.Uri);
    }

    [Fact]
    public void ResourceContents_DeserializationWithUnknownProperty_SkipsProperty()
    {
        // Arrange
        const string Json = """
            {
                "uri": "file:///document.txt",
                "text": "File contents here",
                "mimeType": "text/plain",
                "futureEncoding": "utf-16",
                "futureChecksum": "abc123"
            }
            """;

        // Act
        var deserialized = JsonSerializer.Deserialize<ResourceContents>(Json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var textResource = Assert.IsType<TextResourceContents>(deserialized);
        Assert.Equal("file:///document.txt", textResource.Uri);
        Assert.Equal("File contents here", textResource.Text);
        Assert.Equal("text/plain", textResource.MimeType);
    }

    [Fact]
    public void ProgressNotificationParams_DeserializationWithUnknownProperty_SkipsProperty()
    {
        // Arrange
        const string Json = """
            {
                "progressToken": "token123",
                "progress": 50,
                "newProgressFormat": "percentage",
                "estimatedTimeRemaining": 30
            }
            """;

        // Act
        var deserialized = JsonSerializer.Deserialize<ProgressNotificationParams>(Json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("token123", deserialized.ProgressToken.ToString());
        Assert.Equal(50.0f, deserialized.Progress.Progress);
    }

    [Fact]
    public void PrimitiveSchemaDefinition_DeserializationWithUnknownProperty_SkipsProperty()
    {
        // Arrange
        const string Json = """
            {
                "type": "string",
                "description": "A test string",
                "futureValidation": "regex:.*",
                "futureMaxLength": 100
            }
            """;

        // Act
        var deserialized = JsonSerializer.Deserialize<ElicitRequestParams.PrimitiveSchemaDefinition>(Json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        var stringSchema = Assert.IsType<ElicitRequestParams.StringSchema>(deserialized);
        Assert.Equal("A test string", stringSchema.Description);
    }

    [Fact]
    public void CallToolResult_WithContentBlockContainingUnknownProperties_Succeeds()
    {
        // Arrange - Simulates the real-world bug scenario: a malformed response where
        // structuredContent was incorrectly nested inside content blocks
        const string Json = """
            {
                "content": [
                    {
                        "type": "text",
                        "text": "Tool executed successfully",
                        "structuredContent": {
                            "result": "this was incorrectly placed here"
                        }
                    }
                ],
                "isError": false
            }
            """;

        // Act - Should not throw an exception
        var deserialized = JsonSerializer.Deserialize<CallToolResult>(Json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Single(deserialized.Content);
        var textBlock = Assert.IsType<TextContentBlock>(deserialized.Content[0]);
        Assert.Equal("Tool executed successfully", textBlock.Text);
        Assert.False(deserialized.IsError);
    }

    [Fact]
    public void CallToolResult_WithStructuredContentAtCorrectLevel_PreservesProperty()
    {
        // Arrange - Correct placement of structuredContent at CallToolResult level
        const string Json = """
            {
                "content": [
                    {
                        "type": "text",
                        "text": "Tool executed"
                    }
                ],
                "structuredContent": {
                    "result": "correctly placed here",
                    "value": 42
                },
                "isError": false
            }
            """;

        // Act
        var deserialized = JsonSerializer.Deserialize<CallToolResult>(Json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.StructuredContent);
        Assert.Equal("correctly placed here", deserialized.StructuredContent.Value.GetProperty("result").GetString());
        Assert.Equal(42, deserialized.StructuredContent.Value.GetProperty("value").GetInt32());
    }
}
