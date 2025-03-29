using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using Moq;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Tests.Client;

public class McpClientExtensionsTests : LoggedTest
{
    private readonly Pipe _clientToServerPipe = new();
    private readonly Pipe _serverToClientPipe = new();
    private readonly ServiceProvider _serviceProvider;
    private readonly CancellationTokenSource _cts;
    private readonly Task _serverTask;

    public McpClientExtensionsTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
        ServiceCollection sc = new();
        sc.AddSingleton(LoggerFactory);
        sc.AddMcpServer().WithStdioServerTransport();
        // Call WithStdioServerTransport to get the IMcpServer registration, then overwrite default transport with a pipe transport.
        sc.AddSingleton<ITransport>(new StreamServerTransport(_clientToServerPipe.Reader.AsStream(), _serverToClientPipe.Writer.AsStream()));
        for (int f = 0; f < 10; f++)
        {
            string name = $"Method{f}";
            sc.AddSingleton(McpServerTool.Create((int i) => $"{name} Result {i}", new() { Name = name }));
        }
        sc.AddSingleton(McpServerTool.Create([McpServerTool(Destructive = false, OpenWorld = true)](string i) => $"{i} Result", new() { Name = "ValuesSetViaAttr" }));
        sc.AddSingleton(McpServerTool.Create([McpServerTool(Destructive = false, OpenWorld = true)](string i) => $"{i} Result", new() { Name = "ValuesSetViaOptions", Destructive = true, OpenWorld = false, ReadOnly = true }));
        _serviceProvider = sc.BuildServiceProvider();

        var server = _serviceProvider.GetRequiredService<IMcpServer>();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _serverTask = server.RunAsync(cancellationToken: _cts.Token);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0.7f, 50)]
    [InlineData(1.0f, 100)]
    public async Task CreateSamplingHandler_ShouldHandleTextMessages(float? temperature, int? maxTokens)
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();
        var requestParams = new CreateMessageRequestParams
        {
            Messages =
            [
                new SamplingMessage
                {
                    Role = Role.User,
                    Content = new Content { Type = "text", Text = "Hello" }
                }
            ],
            Temperature = temperature,
            MaxTokens = maxTokens,
            Meta = new RequestParamsMetadata
            {
                ProgressToken = new ProgressToken(),
            }
        };

        var cancellationToken = CancellationToken.None;
        var expectedResponse = new[] {
            new ChatResponseUpdate
            {
                ModelId = "test-model",
                FinishReason = ChatFinishReason.Stop,
                Role = ChatRole.Assistant,
                Contents =
                [
                    new TextContent("Hello, World!") { RawRepresentation = "Hello, World!" }
                ]
            }
        }.ToAsyncEnumerable();

        mockChatClient
            .Setup(client => client.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), cancellationToken))
            .Returns(expectedResponse);

        var handler = McpClientExtensions.CreateSamplingHandler(mockChatClient.Object);

        // Act
        var result = await handler(requestParams, Mock.Of<IProgress<ProgressNotificationValue>>(), cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Hello, World!", result.Content.Text);
        Assert.Equal("test-model", result.Model);
        Assert.Equal("assistant", result.Role);
        Assert.Equal("endTurn", result.StopReason);
    }

    [Fact]
    public async Task CreateSamplingHandler_ShouldHandleImageMessages()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();
        var requestParams = new CreateMessageRequestParams
        {
            Messages =
            [
                new SamplingMessage
                {
                    Role = Role.User,
                    Content = new Content
                    {
                        Type = "image",
                        MimeType = "image/png",
                        Data = Convert.ToBase64String(new byte[] { 1, 2, 3 })
                    }
                }
            ],
            MaxTokens = 100
        };

        const string expectedData = "SGVsbG8sIFdvcmxkIQ==";
        var cancellationToken = CancellationToken.None;
        var expectedResponse = new[] {
            new ChatResponseUpdate
            {
                ModelId = "test-model",
                FinishReason = ChatFinishReason.Stop,
                Role = ChatRole.Assistant,
                Contents =
                [
                    new DataContent($"data:image/png;base64,{expectedData}") { RawRepresentation = "Hello, World!" }
                ]
            }
        }.ToAsyncEnumerable();

        mockChatClient
            .Setup(client => client.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), cancellationToken))
            .Returns(expectedResponse);

        var handler = McpClientExtensions.CreateSamplingHandler(mockChatClient.Object);

        // Act
        var result = await handler(requestParams, Mock.Of<IProgress<ProgressNotificationValue>>(), cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedData, result.Content.Data);
        Assert.Equal("test-model", result.Model);
        Assert.Equal("assistant", result.Role);
        Assert.Equal("endTurn", result.StopReason);
    }

    [Fact]
    public async Task CreateSamplingHandler_ShouldHandleResourceMessages()
    {
        // Arrange
        const string data = "SGVsbG8sIFdvcmxkIQ==";
        string content = $"data:application/octet-stream;base64,{data}";
        var mockChatClient = new Mock<IChatClient>();
        var resource = new BlobResourceContents
        {
            Blob = data,
            MimeType = "application/octet-stream",
            Uri = "data:application/octet-stream"
        };

        var requestParams = new CreateMessageRequestParams
        {
            Messages =
            [
                new SamplingMessage
                {
                    Role = Role.User,
                    Content = new Content
                    {
                        Type = "resource",
                        Resource = resource
                    },
                }
            ],
            MaxTokens = 100
        };

        var cancellationToken = CancellationToken.None;
        var expectedResponse = new[] {
            new ChatResponseUpdate
            {
                ModelId = "test-model",
                FinishReason = ChatFinishReason.Stop,
                AuthorName = "bot",
                Role = ChatRole.Assistant,
                Contents =
                [
                    resource.ToAIContent()
                ]
            }
        }.ToAsyncEnumerable();

        mockChatClient
            .Setup(client => client.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), cancellationToken))
            .Returns(expectedResponse);

        var handler = McpClientExtensions.CreateSamplingHandler(mockChatClient.Object);

        // Act
        var result = await handler(requestParams, Mock.Of<IProgress<ProgressNotificationValue>>(), cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-model", result.Model);
        Assert.Equal(ChatRole.Assistant.ToString(), result.Role);
        Assert.Equal("endTurn", result.StopReason);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        _clientToServerPipe.Writer.Complete();
        _serverToClientPipe.Writer.Complete();

        await _serverTask;

        await _serviceProvider.DisposeAsync();
        _cts.Dispose();
    }

    private async Task<IMcpClient> CreateMcpClientForServer()
    {
        return await McpClientFactory.CreateAsync(
            new McpServerConfig()
            {
                Id = "TestServer",
                Name = "TestServer",
                TransportType = "ignored",
            },
            createTransportFunc: (_, _) => new StreamClientTransport(
                serverInput: _clientToServerPipe.Writer.AsStream(),
                serverOutput: _serverToClientPipe.Reader.AsStream(),
                LoggerFactory),
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ListToolsAsync_AllToolsReturned()
    {
        IMcpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(12, tools.Count);
        var echo = tools.Single(t => t.Name == "Method4");
        var result = await echo.InvokeAsync(new Dictionary<string, object?>() { ["i"] = 42 }, TestContext.Current.CancellationToken);
        Assert.Contains("Method4 Result 42", result?.ToString());

        var valuesSetViaAttr = tools.Single(t => t.Name == "ValuesSetViaAttr");
        Assert.Null(valuesSetViaAttr.ProtocolTool.Annotations?.Title);
        Assert.Null(valuesSetViaAttr.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.Null(valuesSetViaAttr.ProtocolTool.Annotations?.IdempotentHint);
        Assert.False(valuesSetViaAttr.ProtocolTool.Annotations?.DestructiveHint);
        Assert.True(valuesSetViaAttr.ProtocolTool.Annotations?.OpenWorldHint);

        var valuesSetViaOptions = tools.Single(t => t.Name == "ValuesSetViaOptions");
        Assert.Null(valuesSetViaOptions.ProtocolTool.Annotations?.Title);
        Assert.True(valuesSetViaOptions.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.Null(valuesSetViaOptions.ProtocolTool.Annotations?.IdempotentHint);
        Assert.True(valuesSetViaOptions.ProtocolTool.Annotations?.DestructiveHint);
        Assert.False(valuesSetViaOptions.ProtocolTool.Annotations?.OpenWorldHint);
    }

    [Fact]
    public async Task EnumerateToolsAsync_AllToolsReturned()
    {
        IMcpClient client = await CreateMcpClientForServer();

        await foreach (var tool in client.EnumerateToolsAsync(cancellationToken: TestContext.Current.CancellationToken))
        {
            if (tool.Name == "Method4")
            {
                var result = await tool.InvokeAsync(new Dictionary<string, object?>() { ["i"] = 42 }, TestContext.Current.CancellationToken);
                Assert.Contains("Method4 Result 42", result?.ToString());
                return;
            }
        }

        Assert.Fail("Couldn't find target method");
    }

    [Fact]
    public async Task EnumerateToolsAsync_FlowsJsonSerializerOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerOptions.Default);
        IMcpClient client = await CreateMcpClientForServer();
        bool hasTools = false;

        await foreach (var tool in client.EnumerateToolsAsync(options, TestContext.Current.CancellationToken))
        {
            Assert.Same(options, tool.JsonSerializerOptions);
            hasTools = true;
        }

        foreach (var tool in await client.ListToolsAsync(options, TestContext.Current.CancellationToken))
        {
            Assert.Same(options, tool.JsonSerializerOptions);
        }

        Assert.True(hasTools);
    }

    [Fact]
    public async Task EnumerateToolsAsync_HonorsJsonSerializerOptions()
    {
        JsonSerializerOptions emptyOptions = new() { TypeInfoResolver = JsonTypeInfoResolver.Combine() };
        IMcpClient client = await CreateMcpClientForServer();

        var tool = (await client.ListToolsAsync(emptyOptions, TestContext.Current.CancellationToken)).First();
        await Assert.ThrowsAsync<NotSupportedException>(() => tool.InvokeAsync(new Dictionary<string, object?> { ["i"] = 42 }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendRequestAsync_HonorsJsonSerializerOptions()
    {
        JsonSerializerOptions emptyOptions = new() { TypeInfoResolver = JsonTypeInfoResolver.Combine() };
        IMcpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<NotSupportedException>(() => client.SendRequestAsync<CallToolRequestParams, CallToolResponse>("Method4", new() { Name = "tool" }, emptyOptions, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendNotificationAsync_HonorsJsonSerializerOptions()
    {
        JsonSerializerOptions emptyOptions = new() { TypeInfoResolver = JsonTypeInfoResolver.Combine() };
        IMcpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<NotSupportedException>(() => client.SendNotificationAsync("Method4", new { Value = 42 }, emptyOptions, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetPromptsAsync_HonorsJsonSerializerOptions()
    {
        JsonSerializerOptions emptyOptions = new() { TypeInfoResolver = JsonTypeInfoResolver.Combine() };
        IMcpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<NotSupportedException>(() => client.GetPromptAsync("Prompt", new Dictionary<string, object?> { ["i"] = 42 }, emptyOptions, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WithName_ChangesToolName()
    {
        JsonSerializerOptions options = new(JsonSerializerOptions.Default);
        IMcpClient client = await CreateMcpClientForServer();

        var tool = (await client.ListToolsAsync(options, TestContext.Current.CancellationToken)).First();
        var originalName = tool.Name;
        var renamedTool = tool.WithName("RenamedTool");

        Assert.NotNull(renamedTool);
        Assert.Equal("RenamedTool", renamedTool.Name);
        Assert.Equal(originalName, tool?.Name);
    }

    [Fact]
    public async Task WithDescription_ChangesToolDescription()
    {
        JsonSerializerOptions options = new(JsonSerializerOptions.Default);
        IMcpClient client = await CreateMcpClientForServer();
        var tool = (await client.ListToolsAsync(options, TestContext.Current.CancellationToken)).FirstOrDefault();
        var originalDescription = tool?.Description;
        var redescribedTool = tool?.WithDescription("ToolWithNewDescription");
        Assert.NotNull(redescribedTool);
        Assert.Equal("ToolWithNewDescription", redescribedTool.Description);
        Assert.Equal(originalDescription, tool?.Description);
    }
}