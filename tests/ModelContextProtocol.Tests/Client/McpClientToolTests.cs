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
            new()
            { Text = "Simple text result" };

        // Tool that returns only text content (string)
        [McpServerTool]
        public static string StringTool() => "Simple string result";

        // Tool that returns image content as single ContentBlock
        [McpServerTool]
        public static ImageContentBlock ImageTool() =>
            new()
            { Data = System.Text.Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes("fake-image-data"))), MimeType = "image/png" };

        // Tool that returns audio content as single ContentBlock
        [McpServerTool]
        public static AudioContentBlock AudioTool() =>
            new()
            { Data = System.Text.Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes("fake-audio-data"))), MimeType = "audio/mp3" };

        // Tool that returns embedded resource
        [McpServerTool]
        public static EmbeddedResourceBlock EmbeddedResourceTool() =>
            new()
            { Resource = new TextResourceContents { Uri = "resource-uri", Text = "Resource text content", MimeType = "text/plain" } };

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
            new()
            { Uri = "file://test.txt", Name = "test.txt" };

        // Tool that returns mixed content where some can't be converted (ResourceLinkBlock + Image)
        [McpServerTool]
        public static IEnumerable<ContentBlock> MixedWithNonConvertibleTool()
        {
            yield return new ImageContentBlock { Data = System.Text.Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes("image-data"))), MimeType = "image/png" };
            yield return new ResourceLinkBlock { Uri = "file://linked.txt", Name = "linked.txt" };
        }

        // Tool that returns CallToolResult with IsError = true
        [McpServerTool]
        public static CallToolResult ErrorTool() =>
            new()
            {
                IsError = true,
                Content = [new TextContentBlock { Text = "Error message" }]
            };

        // Tool that returns CallToolResult with StructuredContent
        [McpServerTool]
        public static CallToolResult StructuredContentTool() =>
            new()
            {
                Content = [new TextContentBlock { Text = "Regular content" }],
                StructuredContent = JsonElement.Parse("{\"key\":\"value\"}")
            };

        // Tool that returns CallToolResult with Meta
        [McpServerTool]
        public static CallToolResult MetaTool() =>
            new()
            {
                Content = [new TextContentBlock { Text = "Content with meta" }],
                Meta = new JsonObject { ["customKey"] = "customValue" }
            };

        // Tool that returns CallToolResult with multiple properties (IsError + Meta)
        [McpServerTool]
        public static CallToolResult ErrorWithMetaTool() =>
            new()
            {
                IsError = true,
                Content = [new TextContentBlock { Text = "Error with metadata" }],
                Meta = new JsonObject { ["errorCode"] = 500 }
            };

        // Tool that returns binary resource (non-text)
        [McpServerTool]
        public static EmbeddedResourceBlock BinaryResourceTool() =>
            new()
            {
                Resource = new BlobResourceContents
                {
                    Uri = "data://blob",
                    Blob = System.Text.Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes("binary-data"))),
                    MimeType = "application/octet-stream"
                }
            };

        // Tool that echoes back the metadata it receives
        [McpServerTool]
        public static TextContentBlock MetadataEchoTool(RequestContext<CallToolRequestParams> context)
        {
            var meta = context.Params?.Meta;
            var metaJson = meta?.ToJsonString() ?? "{}";
            return new TextContentBlock { Text = metaJson };
        }

        // Tool that accepts arbitrary JsonElement parameter to test anonymous type serialization
        [McpServerTool]
        public static TextContentBlock ArgumentEchoTool(string text, JsonElement coordinates)
        {
            var result = new { text, coordinates };
            return new TextContentBlock { Text = JsonSerializer.Serialize(result) };
        }
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
        JsonElement jsonElement = (JsonElement)result!;
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
        JsonElement jsonElement = (JsonElement)result!;
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
        JsonElement jsonElement = (JsonElement)result!;
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

    [Fact]
    public async Task WithMeta_MetaIsPassedToServer()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "metadata_echo_tool");

        var result = await tool.WithMeta(new()
        {
            ["traceId"] = "test-trace-123",
            ["customKey"] = "customValue"
        }).CallAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.Content);
        var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);

        // The tool echoes back the metadata it received
        var receivedMetadata = JsonNode.Parse(textBlock.Text)?.AsObject();
        Assert.NotNull(receivedMetadata);
        Assert.Equal("test-trace-123", receivedMetadata["traceId"]?.GetValue<string>());
        Assert.Equal("customValue", receivedMetadata["customKey"]?.GetValue<string>());
    }

    [Fact]
    public async Task WithMeta_Null_PreviousMetaIsRemoved()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "metadata_echo_tool");

        var result = await tool.WithMeta(new()
        {
            ["traceId"] = "test-trace-123",
            ["customKey"] = "customValue"
        }).WithMeta(null).CallAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.Content);
        var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);

        var receivedMetadata = JsonNode.Parse(textBlock.Text)?.AsObject();
        Assert.NotNull(receivedMetadata);
        Assert.Null(receivedMetadata["traceId"]?.GetValue<string>());
        Assert.Null(receivedMetadata["customKey"]?.GetValue<string>());
    }

    [Fact]
    public async Task WithMeta_PreviousMetaIsOverwritten()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "metadata_echo_tool");

        var result = await tool.WithMeta(new()
        {
            ["traceId"] = "test-trace-123",
            ["customKey"] = "customValue"
        }).WithMeta(new()
        {
            ["traceId2"] = "abc",
            ["customKey2"] = "def"
        }).CallAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.Content);
        var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);

        var receivedMetadata = JsonNode.Parse(textBlock.Text)?.AsObject();
        Assert.NotNull(receivedMetadata);
        Assert.Null(receivedMetadata["traceId"]?.GetValue<string>());
        Assert.Null(receivedMetadata["customKey"]?.GetValue<string>());
        Assert.Equal("abc", receivedMetadata["traceId2"]?.GetValue<string>());
        Assert.Equal("def", receivedMetadata["customKey2"]?.GetValue<string>());
    }

    [Fact]
    public async Task WithMeta_CreatesNewInstance()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "text_only_tool");

        var toolWithMeta = tool.WithMeta(new() { ["key"] = "value" });

        Assert.NotSame(tool, toolWithMeta);
        Assert.Equal(tool.Name, toolWithMeta.Name);
        Assert.Equal(tool.Description, toolWithMeta.Description);
    }

    [Fact]
    public async Task WithMeta_ChainsWithOtherWithMethods()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "metadata_echo_tool");

        var modifiedTool = tool
            .WithName("custom_name")
            .WithDescription("Custom description")
            .WithMeta(new() { ["chainedKey"] = "chainedValue" });

        Assert.Equal("custom_name", modifiedTool.Name);
        Assert.Equal("Custom description", modifiedTool.Description);

        var result = await modifiedTool.CallAsync(cancellationToken: TestContext.Current.CancellationToken);

        var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
        var receivedMetadata = JsonNode.Parse(textBlock.Text)?.AsObject();
        Assert.NotNull(receivedMetadata);
        Assert.Equal("chainedValue", receivedMetadata["chainedKey"]?.GetValue<string>());
    }

    [Fact]
    public async Task WithMeta_MultipleToolInstancesWithDifferentMetadata()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "metadata_echo_tool");

        var tool1 = tool.WithMeta(new() { ["clientId"] = "client-1" });
        var tool2 = tool.WithMeta(new() { ["clientId"] = "client-2" });

        var result1 = await tool1.CallAsync(cancellationToken: TestContext.Current.CancellationToken);
        var result2 = await tool2.CallAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert - each call should have its own metadata
        var textBlock1 = Assert.IsType<TextContentBlock>(result1.Content[0]);
        var receivedMetadata1 = JsonNode.Parse(textBlock1.Text)?.AsObject();
        Assert.Equal("client-1", receivedMetadata1?["clientId"]?.GetValue<string>());

        var textBlock2 = Assert.IsType<TextContentBlock>(result2.Content[0]);
        var receivedMetadata2 = JsonNode.Parse(textBlock2.Text)?.AsObject();
        Assert.Equal("client-2", receivedMetadata2?["clientId"]?.GetValue<string>());
    }

    [Fact]
    public async Task WithMeta_MergesWithRequestOptionsMeta_NonOverlappingKeys()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "metadata_echo_tool");

        RequestOptions requestOptions = new()
        {
            Meta = new()
            {
                ["requestKey"] = "requestValue"
            }
        };

        var result = await tool.WithMeta(new()
        {
            ["toolKey"] = "toolValue",
            ["sharedContext"] = "fromTool"
        }).CallAsync(options: requestOptions, cancellationToken: TestContext.Current.CancellationToken);

        var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
        var receivedMetadata = JsonNode.Parse(textBlock.Text)?.AsObject();
        Assert.NotNull(receivedMetadata);
        Assert.Equal("toolValue", receivedMetadata["toolKey"]?.GetValue<string>());
        Assert.Equal("requestValue", receivedMetadata["requestKey"]?.GetValue<string>());
        Assert.Equal("fromTool", receivedMetadata["sharedContext"]?.GetValue<string>());
    }

    [Fact]
    public async Task WithMeta_MergesWithRequestOptionsMeta_OverlappingKeys_RequestOptionsWins()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "metadata_echo_tool");

        RequestOptions requestOptions = new()
        {
            Meta = new JsonObject
            {
                ["sharedKey"] = "fromRequestOptions",
                ["requestOnlyKey"] = "requestValue"
            }
        };

        var result = await tool.WithMeta(new()
        {
            ["sharedKey"] = "fromWithMeta",
            ["toolOnlyKey"] = "toolValue"
        }).CallAsync(options: requestOptions, cancellationToken: TestContext.Current.CancellationToken);

        var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
        var receivedMetadata = JsonNode.Parse(textBlock.Text)?.AsObject();
        Assert.NotNull(receivedMetadata);
        
        // RequestOptions should win for the shared key
        Assert.Equal("fromRequestOptions", receivedMetadata["sharedKey"]?.GetValue<string>());
        
        // Non-overlapping keys should both be present
        Assert.Equal("toolValue", receivedMetadata["toolOnlyKey"]?.GetValue<string>());
        Assert.Equal("requestValue", receivedMetadata["requestOnlyKey"]?.GetValue<string>());
    }

    [Fact]
    public async Task WithMeta_WithEmptyRequestOptionsMeta_UsesToolMeta()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "metadata_echo_tool");

        RequestOptions requestOptions = new()
        {
            Meta = new() // Empty meta
        };

        var result = await tool.WithMeta(new()
        {
            ["toolKey"] = "toolValue"
        }).CallAsync(options: requestOptions, cancellationToken: TestContext.Current.CancellationToken);

        var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
        var receivedMetadata = JsonNode.Parse(textBlock.Text)?.AsObject();
        Assert.NotNull(receivedMetadata);
        Assert.Equal("toolValue", receivedMetadata["toolKey"]?.GetValue<string>());
    }

    [Fact]
    public async Task WithMeta_DoesNotMutateOriginalMetadata()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "metadata_echo_tool");

        JsonObject toolMeta = new()
        {
            ["originalKey"] = "originalValue"
        };

        RequestOptions requestOptions = new()
        {
            Meta = new()
            {
                ["newKey"] = "newValue"
            }
        };

        await tool.WithMeta(toolMeta).CallAsync(options: requestOptions, cancellationToken: TestContext.Current.CancellationToken);

        // original toolMeta should not be mutated
        Assert.Single(toolMeta);
        Assert.Equal("originalValue", toolMeta["originalKey"]?.GetValue<string>());
        Assert.False(toolMeta.ContainsKey("newKey"));
    }

    [Fact]
    public async Task WithMeta_MultipleCallsWithDifferentRequestOptions_DoNotInterfere()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "metadata_echo_tool");

        var toolWithMeta = tool.WithMeta(new()
        {
            ["baseKey"] = "baseValue"
        });

        var result1 = await toolWithMeta.CallAsync(
            options: new RequestOptions { Meta = new JsonObject { ["callId"] = "call1" } },
            cancellationToken: TestContext.Current.CancellationToken);

        var result2 = await toolWithMeta.CallAsync(
            options: new RequestOptions { Meta = new JsonObject { ["callId"] = "call2" } },
            cancellationToken: TestContext.Current.CancellationToken);

        var textBlock1 = Assert.IsType<TextContentBlock>(result1.Content[0]);
        var receivedMetadata1 = JsonNode.Parse(textBlock1.Text)?.AsObject();
        Assert.Equal("baseValue", receivedMetadata1?["baseKey"]?.GetValue<string>());
        Assert.Equal("call1", receivedMetadata1?["callId"]?.GetValue<string>());

        var textBlock2 = Assert.IsType<TextContentBlock>(result2.Content[0]);
        var receivedMetadata2 = JsonNode.Parse(textBlock2.Text)?.AsObject();
        Assert.Equal("baseValue", receivedMetadata2?["baseKey"]?.GetValue<string>());
        Assert.Equal("call2", receivedMetadata2?["callId"]?.GetValue<string>());
    }

    [Fact]
    public async Task CallAsync_WithOnlyRequestOptionsMeta_NoWithMeta_WorksCorrectly()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == "metadata_echo_tool");

        RequestOptions requestOptions = new()
        {
            Meta = new()
            {
                ["requestOnlyKey"] = "requestOnlyValue"
            }
        };

        var result = await tool.CallAsync(options: requestOptions, cancellationToken: TestContext.Current.CancellationToken);

        var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
        var receivedMetadata = JsonNode.Parse(textBlock.Text)?.AsObject();
        Assert.NotNull(receivedMetadata);
        Assert.Equal("requestOnlyValue", receivedMetadata["requestOnlyKey"]?.GetValue<string>());
    }

    [Fact]
    public async Task CallToolAsync_WithAnonymousTypeArguments_Works()
    {
        if (!JsonSerializer.IsReflectionEnabledByDefault)
        {
            return;
        }

        await using McpClient client = await CreateMcpClientForServer();

        // Call with dictionary containing anonymous type values
        var arguments = new Dictionary<string, object?>
        {
            ["text"] = "test",
            ["coordinates"] = new { X = 1.0, Y = 2.0 }  // Anonymous type
        };

        // This should not throw NotSupportedException
        var result = await client.CallToolAsync("argument_echo_tool", arguments, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Content);
        
        // Verify the anonymous type was serialized correctly
        var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Contains("coordinates", textBlock.Text);
    }
}
