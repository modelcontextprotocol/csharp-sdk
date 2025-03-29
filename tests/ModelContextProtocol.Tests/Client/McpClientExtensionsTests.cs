using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Configuration;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Transport;
using Moq;
using System.IO.Pipelines;

namespace ModelContextProtocol.Tests.Client;

public class McpClientExtensionsTests
{
    private readonly Pipe _clientToServerPipe = new();
    private readonly Pipe _serverToClientPipe = new();
    private readonly IMcpServer _server;

    public McpClientExtensionsTests()
    {
        ServiceCollection sc = new();
        sc.AddSingleton<IServerTransport>(new StdioServerTransport("TestServer", _clientToServerPipe.Reader.AsStream(), _serverToClientPipe.Writer.AsStream()));
        sc.AddMcpServer();
        for (int f = 0; f < 10; f++)
        {
            string name = $"Method{f}";
            sc.AddSingleton(McpServerTool.Create((int i) => $"{name} Result {i}", new() { Name = name }));
        }
        sc.AddSingleton(McpServerTool.Create([McpServerTool(Destructive = false, OpenWorld = true)](string i) => $"{i} Result", new() { Name = "ValuesSetViaAttr" }));
        sc.AddSingleton(McpServerTool.Create([McpServerTool(Destructive = false, OpenWorld = true)](string i) => $"{i} Result", new() { Name = "ValuesSetViaOptions", Destructive = true, OpenWorld = false, ReadOnly = true }));
        _server = sc.BuildServiceProvider().GetRequiredService<IMcpServer>();
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
            MaxTokens = maxTokens
        };

        var cancellationToken = CancellationToken.None;
        var expectedResponse = new ChatResponse
        {
            Messages = { new ChatMessage { Role = ChatRole.Assistant, Contents = { new TextContent("Hi there!") } } },
            ModelId = "test-model",
            FinishReason = ChatFinishReason.Stop
        };

        mockChatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions>(), cancellationToken))
            .ReturnsAsync(expectedResponse);

        var handler = McpClientExtensions.CreateSamplingHandler(mockChatClient.Object);

        // Act
        var result = await handler(requestParams, cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Hi there!", result.Content.Text);
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
            Messages = new[]
            {
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
        },
            MaxTokens = 100
        };
        var cancellationToken = CancellationToken.None;

        var expectedResponse = new ChatResponse
        {
            Messages = { new ChatMessage { Role = ChatRole.Assistant, Contents = new[] { new TextContent("Image received!") } } },
            ModelId = "test-model",
            FinishReason = ChatFinishReason.Stop
        };

        mockChatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions>(), cancellationToken))
            .ReturnsAsync(expectedResponse);

        var handler = McpClientExtensions.CreateSamplingHandler(mockChatClient.Object);

        // Act
        var result = await handler(requestParams, cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Image received!", result.Content.Text);
        Assert.Equal("test-model", result.Model);
        Assert.Equal("assistant", result.Role);
        Assert.Equal("endTurn", result.StopReason);
    }

    [Fact]
    public async Task CreateSamplingHandler_ShouldHandleResourceMessages()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();
        var requestParams = new CreateMessageRequestParams
        {
            Messages = new[]
            {
            new SamplingMessage
            {
                Role = Role.User,
                Content = new Content
                {
                    Type = "resource",
                    Resource = new ResourceContents
                    {
                        Text = "Resource text",
                        Blob = Convert.ToBase64String(new byte[] { 4, 5, 6 }),
                        MimeType = "application/octet-stream"
                    }
                }
            }
        },
            MaxTokens = 100
        };
        var cancellationToken = CancellationToken.None;

        var expectedResponse = new ChatResponse
        {
            Messages = { new ChatMessage { Role = ChatRole.Assistant, Contents = new[] { new TextContent("Resource processed!") } } },
            ModelId = "test-model",
            FinishReason = ChatFinishReason.Stop
        };

        mockChatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions>(), cancellationToken))
            .ReturnsAsync(expectedResponse);

        var handler = McpClientExtensions.CreateSamplingHandler(mockChatClient.Object);

        // Act
        var result = await handler(requestParams, cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Resource processed!", result.Content.Text);
        Assert.Equal("test-model", result.Model);
        Assert.Equal("assistant", result.Role);
        Assert.Equal("endTurn", result.StopReason);
    }

    public ValueTask DisposeAsync()
    {
        _clientToServerPipe.Writer.Complete();
        _serverToClientPipe.Writer.Complete();
        return _server.DisposeAsync();
    }

    private async Task<IMcpClient> CreateMcpClientForServer()
    {
        await _server.StartAsync(TestContext.Current.CancellationToken);

        var serverStdinWriter = new StreamWriter(_clientToServerPipe.Writer.AsStream());
        var serverStdoutReader = new StreamReader(_serverToClientPipe.Reader.AsStream());

        var serverConfig = new McpServerConfig()
        {
            Id = "TestServer",
            Name = "TestServer",
            TransportType = "ignored",
        };

        return await McpClientFactory.CreateAsync(
            serverConfig,
            createTransportFunc: (_, _) => new StreamClientTransport(serverStdinWriter, serverStdoutReader),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ListToolsAsync_AllToolsReturned()
    {
        IMcpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(TestContext.Current.CancellationToken);
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

        await foreach (var tool in client.EnumerateToolsAsync(TestContext.Current.CancellationToken))
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
}