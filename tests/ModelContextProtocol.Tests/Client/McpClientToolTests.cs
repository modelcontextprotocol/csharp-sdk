using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Client;

public class McpClientToolTests : ClientServerTestBase
{
    public McpClientToolTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithTools<TestTools>();
    }

    private class TestTools
    {
        // Tool that returns only text content
        [McpServerTool]
        public static TextContentBlock TextOnlyTool() =>
            new TextContentBlock { Text = "Simple text result" };

        // Tool that returns only text content (string)
        [McpServerTool]
        public static string StringTool() => "Simple string result";

        // Tool that returns image content as single ContentBlock
        [McpServerTool]
        public static ImageContentBlock ImageTool() =>
            new ImageContentBlock { Data = Convert.ToBase64String(Encoding.UTF8.GetBytes("fake-image-data")), MimeType = "image/png" };

        // Tool that returns audio content as single ContentBlock
        [McpServerTool]
        public static AudioContentBlock AudioTool() =>
            new AudioContentBlock { Data = Convert.ToBase64String(Encoding.UTF8.GetBytes("fake-audio-data")), MimeType = "audio/mp3" };

        // Tool that returns embedded resource
        [McpServerTool]
        public static EmbeddedResourceBlock EmbeddedResourceTool() =>
            new EmbeddedResourceBlock { Resource = new TextResourceContents { Uri = "resource-uri", Text = "Resource text content", MimeType = "text/plain" } };

        // Tool that returns mixed content (text + image) using IEnumerable<AIContent>
        [McpServerTool]
        public static IEnumerable<AIContent> MixedContentTool()
        {
            yield return new TextContent("Description of the image");
            yield return new DataContent(Encoding.UTF8.GetBytes("fake-image-data"), "image/png");
        }

        // Tool that returns multiple images using IEnumerable<AIContent>
        [McpServerTool]
        public static IEnumerable<AIContent> MultipleImagesTool()
        {
            yield return new DataContent(Encoding.UTF8.GetBytes("image1"), "image/png");
            yield return new DataContent(Encoding.UTF8.GetBytes("image2"), "image/jpeg");
        }

        // Tool that returns audio + text using IEnumerable<AIContent>
        [McpServerTool]
        public static IEnumerable<AIContent> AudioWithTextTool()
        {
            yield return new TextContent("Audio transcription");
            yield return new DataContent(Encoding.UTF8.GetBytes("fake-audio"), "audio/wav");
        }

        // Tool that returns embedded resource + text using IEnumerable<ContentBlock>
        [McpServerTool]
        public static IEnumerable<ContentBlock> ResourceWithTextTool()
        {
            yield return new TextContentBlock { Text = "Resource description" };
            yield return new EmbeddedResourceBlock { Resource = new TextResourceContents { Uri = "file://test.txt", Text = "File content", MimeType = "text/plain" } };
        }

        // Tool that returns all content types using IEnumerable<AIContent>
        [McpServerTool]
        public static IEnumerable<AIContent> AllContentTypesTool()
        {
            yield return new TextContent("Mixed content");
            yield return new DataContent(Encoding.UTF8.GetBytes("image"), "image/png");
            yield return new DataContent(Encoding.UTF8.GetBytes("audio"), "audio/mp3");
            yield return new DataContent(Encoding.UTF8.GetBytes("blob"), "application/octet-stream");
        }

        // Tool that returns content that can't be converted to AIContent (ResourceLinkBlock)
        [McpServerTool]
        public static ResourceLinkBlock ResourceLinkTool() =>
            new ResourceLinkBlock { Uri = "file://test.txt", Name = "test.txt" };

        // Tool that returns mixed content where some can't be converted (ResourceLinkBlock + Image)
        [McpServerTool]
        public static IEnumerable<ContentBlock> MixedWithNonConvertibleTool()
        {
            yield return new ImageContentBlock { Data = Convert.ToBase64String(Encoding.UTF8.GetBytes("image-data")), MimeType = "image/png" };
            yield return new ResourceLinkBlock { Uri = "file://linked.txt", Name = "linked.txt" };
        }

        // Tool that returns CallToolResult with IsError = true
        [McpServerTool]
        public static CallToolResult ErrorTool() =>
            new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = "Error message" }]
            };

        // Tool that returns CallToolResult with StructuredContent
        [McpServerTool]
        public static CallToolResult StructuredContentTool() =>
            new CallToolResult
            {
                Content = [new TextContentBlock { Text = "Regular content" }],
                StructuredContent = JsonNode.Parse("{\"key\":\"value\"}")
            };

        // Tool that returns CallToolResult with Meta
        [McpServerTool]
        public static CallToolResult MetaTool() =>
            new CallToolResult
            {
                Content = [new TextContentBlock { Text = "Content with meta" }],
                Meta = new JsonObject { ["customKey"] = "customValue" }
            };

        // Tool that returns CallToolResult with multiple properties (IsError + Meta)
        [McpServerTool]
        public static CallToolResult ErrorWithMetaTool() =>
            new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = "Error with metadata" }],
                Meta = new JsonObject { ["errorCode"] = 500 }
            };

        // Tool that returns binary resource (non-text)
        [McpServerTool]
        public static EmbeddedResourceBlock BinaryResourceTool() =>
            new EmbeddedResourceBlock
            {
                Resource = new BlobResourceContents
                {
                    Uri = "data://blob",
                    Blob = Convert.ToBase64String(Encoding.UTF8.GetBytes("binary-data")),
                    MimeType = "application/octet-stream"
                }
            };
    }

    [Fact]
    public async Task TextOnlyTool_ReturnsSingleTextContent()
    {
        // Arrange
        await using McpClient client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "text_only_tool");

        // Act
        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert - single text content should return TextContent
        var textContent = Assert.IsType<TextContent>(result);
        Assert.Equal("Simple text result", textContent.Text);
    }

    [Fact]
    public async Task StringTool_ReturnsSingleTextContent()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "string_tool");

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        var textContent = Assert.IsType<TextContent>(result);
        Assert.Equal("Simple string result", textContent.Text);
    }

    [Fact]
    public async Task ImageTool_ReturnsSingleDataContent()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "image_tool");

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        var dataContent = Assert.IsType<DataContent>(result);
        Assert.Equal("image/png", dataContent.MediaType);
        Assert.Equal("fake-image-data", Encoding.UTF8.GetString(dataContent.Data.ToArray()));
    }

    [Fact]
    public async Task AudioTool_ReturnsSingleDataContent()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "audio_tool");

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        var dataContent = Assert.IsType<DataContent>(result);
        Assert.Equal("audio/mp3", dataContent.MediaType);
        Assert.Equal("fake-audio-data", Encoding.UTF8.GetString(dataContent.Data.ToArray()));
    }

    [Fact]
    public async Task EmbeddedResourceTool_ReturnsSingleTextContent()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "embedded_resource_tool");

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        var textContent = Assert.IsType<TextContent>(result);
        Assert.Equal("Resource text content", textContent.Text);
    }

    [Fact]
    public async Task MixedContentTool_ReturnsAIContentArray()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "mixed_content_tool");

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        var aiContents = Assert.IsType<AIContent[]>(result);
        Assert.Equal(2, aiContents.Length);
        
        var textContent = Assert.IsType<TextContent>(aiContents[0]);
        Assert.Equal("Description of the image", textContent.Text);
        
        var dataContent = Assert.IsType<DataContent>(aiContents[1]);
        Assert.Equal("image/png", dataContent.MediaType);
    }

    [Fact]
    public async Task MultipleImagesTool_ReturnsAIContentArray()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "multiple_images_tool");

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        var aiContents = Assert.IsType<AIContent[]>(result);
        Assert.Equal(2, aiContents.Length);
        
        var dataContent0 = Assert.IsType<DataContent>(aiContents[0]);
        Assert.Equal("image/png", dataContent0.MediaType);
        Assert.Equal("image1", Encoding.UTF8.GetString(dataContent0.Data.ToArray()));
        
        var dataContent1 = Assert.IsType<DataContent>(aiContents[1]);
        Assert.Equal("image/jpeg", dataContent1.MediaType);
        Assert.Equal("image2", Encoding.UTF8.GetString(dataContent1.Data.ToArray()));
    }

    [Fact]
    public async Task AudioWithTextTool_ReturnsAIContentArray()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "audio_with_text_tool");

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        var aiContents = Assert.IsType<AIContent[]>(result);
        Assert.Equal(2, aiContents.Length);
        
        var textContent = Assert.IsType<TextContent>(aiContents[0]);
        Assert.Equal("Audio transcription", textContent.Text);
        
        var dataContent = Assert.IsType<DataContent>(aiContents[1]);
        Assert.Equal("audio/wav", dataContent.MediaType);
    }

    [Fact]
    public async Task ResourceWithTextTool_ReturnsAIContentArray()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "resource_with_text_tool");

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        var aiContents = Assert.IsType<AIContent[]>(result);
        Assert.Equal(2, aiContents.Length);
        
        var textContent0 = Assert.IsType<TextContent>(aiContents[0]);
        Assert.Equal("Resource description", textContent0.Text);
        
        var textContent1 = Assert.IsType<TextContent>(aiContents[1]);
        Assert.Equal("File content", textContent1.Text);
    }

    [Fact]
    public async Task AllContentTypesTool_ReturnsAIContentArray()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "all_content_types_tool");

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        var aiContents = Assert.IsType<AIContent[]>(result);
        Assert.Equal(4, aiContents.Length);
        
        var textContent = Assert.IsType<TextContent>(aiContents[0]);
        Assert.Equal("Mixed content", textContent.Text);
        
        var dataContent1 = Assert.IsType<DataContent>(aiContents[1]);
        Assert.Equal("image/png", dataContent1.MediaType);
        
        var dataContent2 = Assert.IsType<DataContent>(aiContents[2]);
        Assert.Equal("audio/mp3", dataContent2.MediaType);
        
        var dataContent3 = Assert.IsType<DataContent>(aiContents[3]);
        Assert.Equal("application/octet-stream", dataContent3.MediaType);
    }

    [Fact]
    public async Task SingleAIContent_PreservesRawRepresentation()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "image_tool");

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        var dataContent = Assert.IsType<DataContent>(result);
        Assert.NotNull(dataContent.RawRepresentation);
        var imageBlock = Assert.IsType<ImageContentBlock>(dataContent.RawRepresentation);
        Assert.Equal("image/png", imageBlock.MimeType);
    }

    [Fact]
    public async Task ResourceLinkTool_ReturnsJsonElement()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "resource_link_tool");

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<JsonElement>(result);
        var jsonElement = (JsonElement)result!;
        Assert.True(jsonElement.TryGetProperty("content", out var contentValue));
        Assert.Equal(JsonValueKind.Array, contentValue.ValueKind);
        
        Assert.Equal(1, contentValue.GetArrayLength());
    }

    [Fact]
    public async Task MixedWithNonConvertibleTool_ReturnsJsonElement()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "mixed_with_non_convertible_tool");

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        var jsonElement = Assert.IsType<JsonElement>(result);
        Assert.True(jsonElement.TryGetProperty("content", out var contentArray));
        Assert.Equal(JsonValueKind.Array, contentArray.ValueKind);
        Assert.Equal(2, contentArray.GetArrayLength());
        
        var firstContent = contentArray[0];
        Assert.True(firstContent.TryGetProperty("type", out var type1));
        Assert.Equal("image", type1.GetString());
        
        var secondContent = contentArray[1];
        Assert.True(secondContent.TryGetProperty("type", out var type2));
        Assert.Equal("resource_link", type2.GetString());
    }

    [Fact]
    public async Task ErrorTool_ReturnsJsonElement()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "error_tool");

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<JsonElement>(result);
        var jsonElement = (JsonElement)result!;
        Assert.True(jsonElement.TryGetProperty("isError", out var isError));
        Assert.True(isError.GetBoolean());
        Assert.True(jsonElement.TryGetProperty("content", out var content));
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
    }

    [Fact]
    public async Task StructuredContentTool_ReturnsJsonElement()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "structured_content_tool");

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        var jsonElement = Assert.IsType<JsonElement>(result);
        Assert.True(jsonElement.TryGetProperty("structuredContent", out var structuredContent));
        Assert.True(structuredContent.TryGetProperty("key", out var key));
        Assert.Equal("value", key.GetString());
        Assert.True(jsonElement.TryGetProperty("content", out var content));
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
    }

    [Fact]
    public async Task MetaTool_ReturnsJsonElement()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "meta_tool");

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        var jsonElement = Assert.IsType<JsonElement>(result);
        Assert.True(jsonElement.TryGetProperty("_meta", out var meta));
        Assert.True(meta.TryGetProperty("customKey", out var customKey));
        Assert.Equal("customValue", customKey.GetString());
        Assert.True(jsonElement.TryGetProperty("content", out var content));
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
    }

    [Fact]
    public async Task ErrorWithMetaTool_ReturnsJsonElement()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "error_with_meta_tool");

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<JsonElement>(result);
        var jsonElement = (JsonElement)result!;
        Assert.True(jsonElement.TryGetProperty("isError", out var isError));
        Assert.True(isError.GetBoolean());
        Assert.True(jsonElement.TryGetProperty("_meta", out var meta));
        Assert.True(meta.TryGetProperty("errorCode", out var errorCode));
        Assert.Equal(500, errorCode.GetInt32());
        Assert.True(jsonElement.TryGetProperty("content", out var content));
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
    }

    [Fact]
    public async Task BinaryResourceTool_ReturnsSingleDataContent()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "binary_resource_tool");

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        var dataContent = Assert.IsType<DataContent>(result);
        Assert.Equal("application/octet-stream", dataContent.MediaType);
        Assert.Equal("binary-data", Encoding.UTF8.GetString(dataContent.Data.ToArray()));
    }

    [Fact]
    public async Task MultipleAIContent_PreservesRawRepresentation()
    {
        await using McpClient client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "mixed_content_tool");

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        var aiContents = Assert.IsType<AIContent[]>(result);
        Assert.Equal(2, aiContents.Length);

        var textContent = Assert.IsType<TextContent>(aiContents[0]);
        Assert.NotNull(textContent.RawRepresentation);
        Assert.IsType<TextContentBlock>(textContent.RawRepresentation);

        var dataContent = Assert.IsType<DataContent>(aiContents[1]);
        Assert.NotNull(dataContent.RawRepresentation);
        Assert.IsType<ImageContentBlock>(dataContent.RawRepresentation);
    }
}
