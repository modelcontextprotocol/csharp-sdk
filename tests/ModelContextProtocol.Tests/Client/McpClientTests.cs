using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;

namespace ModelContextProtocol.Tests.Client;

public class McpClientTests : ClientServerTestBase
{
    public McpClientTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        for (int f = 0; f < 10; f++)
        {
            string name = $"Method{f}";
            mcpServerBuilder.WithTools([McpServerTool.Create((int i) => $"{name} Result {i}", new() { Name = name })]);
        }
        mcpServerBuilder.WithTools([McpServerTool.Create([McpServerTool(Destructive = false, OpenWorld = true)] (string i) => $"{i} Result", new() { Name = "ValuesSetViaAttr" })]);
        mcpServerBuilder.WithTools([McpServerTool.Create([McpServerTool(Destructive = false, OpenWorld = true)] (string i) => $"{i} Result", new() { Name = "ValuesSetViaOptions", Destructive = true, OpenWorld = false, ReadOnly = true })]);
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
                    Content = new TextContentBlock { Text = "Hello" }
                }
            ],
            Temperature = temperature,
            MaxTokens = maxTokens,
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
        Assert.Equal("Hello, World!", (result.Content as TextContentBlock)?.Text);
        Assert.Equal("test-model", result.Model);
        Assert.Equal(Role.Assistant, result.Role);
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
                    Content = new ImageContentBlock
                    {
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
        Assert.Equal(expectedData, (result.Content as ImageContentBlock)?.Data);
        Assert.Equal("test-model", result.Model);
        Assert.Equal(Role.Assistant, result.Role);
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
                    Content = new EmbeddedResourceBlock { Resource = resource },
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
        Assert.Equal(Role.Assistant, result.Role);
        Assert.Equal("endTurn", result.StopReason);
    }

    [Fact]
    public async Task ListToolsAsync_AllToolsReturned()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(12, tools.Count);
        var echo = tools.Single(t => t.Name == "Method4");
        var result = await echo.InvokeAsync(new() { ["i"] = 42 }, TestContext.Current.CancellationToken);
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
        await using McpClient client = await CreateMcpClientForServer();

        await foreach (var tool in client.EnumerateToolsAsync(cancellationToken: TestContext.Current.CancellationToken))
        {
            if (tool.Name == "Method4")
            {
                var result = await tool.InvokeAsync(new() { ["i"] = 42 }, TestContext.Current.CancellationToken);
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
        await using McpClient client = await CreateMcpClientForServer();
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
        await using McpClient client = await CreateMcpClientForServer();

        var tool = (await client.ListToolsAsync(emptyOptions, TestContext.Current.CancellationToken)).First();
        await Assert.ThrowsAsync<NotSupportedException>(async () => await tool.InvokeAsync(new() { ["i"] = 42 }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendRequestAsync_HonorsJsonSerializerOptions()
    {
        JsonSerializerOptions emptyOptions = new() { TypeInfoResolver = JsonTypeInfoResolver.Combine() };
        await using McpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<NotSupportedException>(async () => await client.SendRequestAsync<CallToolRequestParams, CallToolResult>("Method4", new() { Name = "tool" }, emptyOptions, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendNotificationAsync_HonorsJsonSerializerOptions()
    {
        JsonSerializerOptions emptyOptions = new() { TypeInfoResolver = JsonTypeInfoResolver.Combine() };
        await using McpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<NotSupportedException>(() => client.SendNotificationAsync("Method4", new { Value = 42 }, emptyOptions, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetPromptsAsync_HonorsJsonSerializerOptions()
    {
        JsonSerializerOptions emptyOptions = new() { TypeInfoResolver = JsonTypeInfoResolver.Combine() };
        await using McpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<NotSupportedException>(async () => await client.GetPromptAsync("Prompt", new Dictionary<string, object?> { ["i"] = 42 }, emptyOptions, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WithName_ChangesToolName()
    {
        JsonSerializerOptions options = new(JsonSerializerOptions.Default);
        await using McpClient client = await CreateMcpClientForServer();

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
        await using McpClient client = await CreateMcpClientForServer();
        var tool = (await client.ListToolsAsync(options, TestContext.Current.CancellationToken)).FirstOrDefault();
        var originalDescription = tool?.Description;
        var redescribedTool = tool?.WithDescription("ToolWithNewDescription");
        Assert.NotNull(redescribedTool);
        Assert.Equal("ToolWithNewDescription", redescribedTool.Description);
        Assert.Equal(originalDescription, tool?.Description);
    }

    [Fact]
    public async Task WithProgress_ProgressReported()
    {
        const int TotalNotifications = 3;
        int remainingProgress = TotalNotifications;
        TaskCompletionSource<bool> allProgressReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Server.ServerOptions.Capabilities?.Tools?.ToolCollection?.Add(McpServerTool.Create(async (IProgress<ProgressNotificationValue> progress) =>
        {
            for (int i = 0; i < TotalNotifications; i++)
            {
                progress.Report(new ProgressNotificationValue { Progress = i * 10, Message = "making progress" });
                await Task.Delay(1);
            }

            await allProgressReceived.Task;

            return 42;
        }, new() { Name = "ProgressReporter" }));

        await using McpClient client = await CreateMcpClientForServer();

        var tool = (await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).First(t => t.Name == "ProgressReporter");

        IProgress<ProgressNotificationValue> progress = new SynchronousProgress(value =>
        {
            Assert.True(value.Progress >= 0 && value.Progress <= 100);
            Assert.Equal("making progress", value.Message);
            if (Interlocked.Decrement(ref remainingProgress) == 0)
            {
                allProgressReceived.SetResult(true);
            }
        });

        Assert.Throws<ArgumentNullException>("progress", () => tool.WithProgress(null!));

        var result = await tool.WithProgress(progress).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("42", result?.ToString());
    }

    private sealed class SynchronousProgress(Action<ProgressNotificationValue> callback) : IProgress<ProgressNotificationValue>
    {
        public void Report(ProgressNotificationValue value) => callback(value);
    }

    [Fact]
    public async Task AsClientLoggerProvider_MessagesSentToClient()
    {
        await using McpClient client = await CreateMcpClientForServer();

        ILoggerProvider loggerProvider = Server.AsClientLoggerProvider();
        Assert.Throws<ArgumentNullException>("categoryName", () => loggerProvider.CreateLogger(null!));

        ILogger logger = loggerProvider.CreateLogger("TestLogger");
        Assert.NotNull(logger);

        Assert.Null(logger.BeginScope(""));

        Assert.Null(Server.LoggingLevel);
        Assert.False(logger.IsEnabled(LogLevel.Trace));
        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.False(logger.IsEnabled(LogLevel.Warning));
        Assert.False(logger.IsEnabled(LogLevel.Error));
        Assert.False(logger.IsEnabled(LogLevel.Critical));

        await client.SetLoggingLevel(LoggingLevel.Info, TestContext.Current.CancellationToken);

        DateTime start = DateTime.UtcNow;
        while (Server.LoggingLevel is null)
        {
            await Task.Delay(1, TestContext.Current.CancellationToken);
            Assert.True(DateTime.UtcNow - start < TimeSpan.FromSeconds(10), "Timed out waiting for logging level to be set");
        }

        Assert.Equal(LoggingLevel.Info, Server.LoggingLevel);
        Assert.False(logger.IsEnabled(LogLevel.Trace));
        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.True(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
        Assert.True(logger.IsEnabled(LogLevel.Error));
        Assert.True(logger.IsEnabled(LogLevel.Critical));

        List<string> data = [];
        var channel = Channel.CreateUnbounded<LoggingMessageNotificationParams?>();

        await using (client.RegisterNotificationHandler(NotificationMethods.LoggingMessageNotification,
            (notification, cancellationToken) =>
            {
                Assert.True(channel.Writer.TryWrite(JsonSerializer.Deserialize<LoggingMessageNotificationParams>(notification.Params, McpJsonUtilities.DefaultOptions)));
                return default;
            }))
        {
            logger.LogTrace("Trace {Message}", "message");
            logger.LogDebug("Debug {Message}", "message");
            logger.LogInformation("Information {Message}", "message");
            logger.LogWarning("Warning {Message}", "message");
            logger.LogError("Error {Message}", "message");
            logger.LogCritical("Critical {Message}", "message");

            for (int i = 0; i < 4; i++)
            {
                var m = await channel.Reader.ReadAsync(TestContext.Current.CancellationToken);
                Assert.NotNull(m);
                Assert.NotNull(m.Data);

                Assert.Equal("TestLogger", m.Logger);

                string ? s = JsonSerializer.Deserialize<string>(m.Data.Value, McpJsonUtilities.DefaultOptions);
                Assert.NotNull(s);

                if (s.Contains("Information"))
                {
                    Assert.Equal(LoggingLevel.Info, m.Level);
                }
                else if (s.Contains("Warning"))
                {
                    Assert.Equal(LoggingLevel.Warning, m.Level);
                }
                else if (s.Contains("Error"))
                {
                    Assert.Equal(LoggingLevel.Error, m.Level);
                }
                else if (s.Contains("Critical"))
                {
                    Assert.Equal(LoggingLevel.Critical, m.Level);
                }

                data.Add(s);
            }

            channel.Writer.Complete();
        }

        Assert.False(await channel.Reader.WaitToReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            [
                "Critical message",
                "Error message",
                "Information message",
                "Warning message",
            ], 
            data.OrderBy(s => s));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2025-03-26")]
    public async Task ReturnsNegotiatedProtocolVersion(string? protocolVersion)
    {
        await using McpClient client = await CreateMcpClientForServer(new() { ProtocolVersion = protocolVersion });
        Assert.Equal(protocolVersion ?? "2025-06-18", client.NegotiatedProtocolVersion);
    }
}