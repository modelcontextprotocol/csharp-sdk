using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using System.Runtime.CompilerServices;
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

        services.Configure<McpServerOptions>(o =>
        {
            o.ServerInfo = new Implementation
            {
                Name = "test-server",
                Version = "1.0.0",
                Description = "A test server for unit testing",
                WebsiteUrl = "https://example.com",
                Icons =
                [
                    new Icon { Source = "https://example.com/icon-48.png", MimeType = "image/png", Sizes = ["48x48"], Theme = "light" },
                    new Icon { Source = "https://example.com/icon.svg", MimeType = "image/svg+xml", Sizes = ["any"], Theme = "dark" }
                ]
            };
        });
    }

    [Fact]
    public async Task CanReadServerInfo()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var serverInfo = client.ServerInfo;
        Assert.Equal("test-server", serverInfo.Name);
        Assert.Equal("1.0.0", serverInfo.Version);
        Assert.Equal("A test server for unit testing", serverInfo.Description);
        Assert.Equal("https://example.com", serverInfo.WebsiteUrl);
        Assert.NotNull(serverInfo.Icons);
        Assert.Equal(2, serverInfo.Icons.Count);

        var icon0 = serverInfo.Icons[0];
        Assert.Equal("https://example.com/icon-48.png", icon0.Source);
        Assert.Equal("image/png", icon0.MimeType);
        Assert.Single(icon0.Sizes!, "48x48");
        Assert.Equal("light", icon0.Theme);

        var icon1 = serverInfo.Icons[1];
        Assert.Equal("https://example.com/icon.svg", icon1.Source);
        Assert.Equal("image/svg+xml", icon1.MimeType);
        Assert.Single(icon1.Sizes!, "any");
        Assert.Equal("dark", icon1.Theme);
    }

    [Fact]
    public async Task ServerCanReadClientInfo()
    {
        var clientOptions = new McpClientOptions
        {
            ClientInfo = new Implementation
            {
                Name = "test-client",
                Version = "2.0.0",
                Description = "A test client for validating client-server communication"
            }
        };

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        // Verify the server received the client info with description
        var clientInfo = Server.ClientInfo;
        Assert.NotNull(clientInfo);
        Assert.Equal("test-client", clientInfo.Name);
        Assert.Equal("2.0.0", clientInfo.Version);
        Assert.Equal("A test client for validating client-server communication", clientInfo.Description);
    }

    [Theory]
    [InlineData(null, 10)]
    [InlineData(0.7f, 50)]
    [InlineData(1.0f, 100)]
    public async Task CreateSamplingHandler_ShouldHandleTextMessages(float? temperature, int maxTokens)
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
                    Content = [new TextContentBlock { Text = "Hello" }]
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

        var handler = mockChatClient.Object.CreateSamplingHandler();

        // Act
        var result = await handler(requestParams, Mock.Of<IProgress<ProgressNotificationValue>>(), cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Hello, World!", result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text);
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
                    Content = [ImageContentBlock.FromBytes((byte[])[1, 2, 3], "image/png")],
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

        var handler = mockChatClient.Object.CreateSamplingHandler();

        // Act
        var result = await handler(requestParams, Mock.Of<IProgress<ProgressNotificationValue>>(), cancellationToken);

        // Assert
        Assert.NotNull(result);
        var imageData = result.Content.OfType<ImageContentBlock>().FirstOrDefault()?.Data.ToArray() ?? [];
        Assert.Equal(expectedData, System.Text.Encoding.UTF8.GetString(imageData));
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
            Blob = System.Text.Encoding.UTF8.GetBytes(data),
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
                    Content = [new EmbeddedResourceBlock { Resource = resource }],
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

        var handler = mockChatClient.Object.CreateSamplingHandler();

        // Act
        var result = await handler(requestParams, Mock.Of<IProgress<ProgressNotificationValue>>(), cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-model", result.Model);
        Assert.Equal(Role.Assistant, result.Role);
        Assert.Equal("endTurn", result.StopReason);
    }

    [Fact]
    public async Task CreateSamplingHandler_ShouldUseToolRoleForToolResultMessages()
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
                    Content = [new TextContentBlock { Text = "What is the weather in Paris?" }]
                },
                new SamplingMessage
                {
                    Role = Role.Assistant,
                    Content = [new ToolUseContentBlock
                    {
                        Id = "call_weather_123",
                        Name = "get_weather",
                        Input = JsonElement.Parse("""{"location":"Paris"}""")
                    }]
                },
                new SamplingMessage
                {
                    Role = Role.User,
                    Content = [new ToolResultContentBlock
                    {
                        ToolUseId = "call_weather_123",
                        Content = [new TextContentBlock { Text = "Weather: 18°C, sunny" }]
                    }]
                },
                new SamplingMessage
                {
                    Role = Role.User,
                    Content =
                    [
                        new ToolResultContentBlock
                        {
                            ToolUseId = "call_mixed_123",
                            Content = [new TextContentBlock { Text = "Tool result" }]
                        },
                        new TextContentBlock { Text = "Additional text content" }
                    ]
                }
            ],
            MaxTokens = 100
        };

        IEnumerable<ChatMessage>? capturedMessages = null;
        var cancellationToken = CancellationToken.None;
        var expectedResponse = new[] {
            new ChatResponseUpdate
            {
                ModelId = "test-model",
                FinishReason = ChatFinishReason.Stop,
                Role = ChatRole.Assistant,
                Contents = [new TextContent("The weather in Paris is 18°C and sunny.")]
            }
        }.ToAsyncEnumerable();

        mockChatClient
            .Setup(client => client.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), cancellationToken))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((messages, _, _) => capturedMessages = messages.ToList())
            .Returns(expectedResponse);

        var handler = mockChatClient.Object.CreateSamplingHandler();

        // Act
        var result = await handler(requestParams, Mock.Of<IProgress<ProgressNotificationValue>>(), cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(capturedMessages);
        var messagesList = capturedMessages.ToList();
        Assert.Equal(4, messagesList.Count);

        // First message should be User role (text message)
        Assert.Equal(ChatRole.User, messagesList[0].Role);
        Assert.IsType<TextContent>(messagesList[0].Contents.Single());

        // Second message should be Assistant role (tool use)
        Assert.Equal(ChatRole.Assistant, messagesList[1].Role);
        Assert.IsType<FunctionCallContent>(messagesList[1].Contents.Single());

        // Third message should be Tool role (tool result only) - this is the bug fix
        Assert.Equal(ChatRole.Tool, messagesList[2].Role);
        Assert.IsType<FunctionResultContent>(messagesList[2].Contents.Single());

        // Fourth message should be User role (mixed content: tool result + text)
        Assert.Equal(ChatRole.User, messagesList[3].Role);
        Assert.Equal(2, messagesList[3].Contents.Count);
        Assert.Contains(messagesList[3].Contents, c => c is FunctionResultContent);
        Assert.Contains(messagesList[3].Contents, c => c is TextContent);
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

        await Assert.ThrowsAsync<NotSupportedException>(async () => await client.GetPromptAsync("Prompt", new Dictionary<string, object?> { ["i"] = 42 }, new RequestOptions { JsonSerializerOptions = emptyOptions }, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WithName_ChangesToolName()
    {
        JsonSerializerOptions options = new(JsonSerializerOptions.Default);
        await using McpClient client = await CreateMcpClientForServer();

        var tool = (await client.ListToolsAsync(new RequestOptions { JsonSerializerOptions = options }, TestContext.Current.CancellationToken)).First();
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
        var tool = (await client.ListToolsAsync(new RequestOptions { JsonSerializerOptions = options }, TestContext.Current.CancellationToken)).FirstOrDefault();
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

        Server.ServerOptions.ToolCollection?.Add(McpServerTool.Create(async (IProgress<ProgressNotificationValue> progress) =>
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

        await client.SetLoggingLevelAsync(LoggingLevel.Info, options: null, TestContext.Current.CancellationToken);

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
                Assert.Equal("TestLogger", m.Logger);

                string? s = JsonSerializer.Deserialize<string>(m.Data, McpJsonUtilities.DefaultOptions);
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
    [InlineData("2025-06-18")]
    public async Task ReturnsNegotiatedProtocolVersion(string? protocolVersion)
    {
        await using McpClient client = await CreateMcpClientForServer(new() { ProtocolVersion = protocolVersion });
        Assert.Equal(protocolVersion ?? "2025-11-25", client.NegotiatedProtocolVersion);
    }

    [Fact]
    public async Task EndToEnd_SamplingWithTools_ServerUsesIChatClientWithFunctionInvocation_ClientHandlesSamplingWithIChatClient()
    {
        int getWeatherToolCallCount = 0;
        int askClientToolCallCount = 0;

        Server.ServerOptions.ToolCollection?.Add(McpServerTool.Create(
            async (McpServer server, string query, CancellationToken cancellationToken) =>
            {
                askClientToolCallCount++;

                var weatherTool = AIFunctionFactory.Create(
                    (string location) =>
                    {
                        getWeatherToolCallCount++;
                        return $"Weather in {location}: sunny, 22°C";
                    },
                    "get_weather", "Gets the weather for a location");

                var response = await server
                    .AsSamplingChatClient()
                    .AsBuilder()
                    .UseFunctionInvocation()
                    .Build()
                    .GetResponseAsync(query, new ChatOptions { Tools = [weatherTool] }, cancellationToken);

                return response.Text ?? "No response";
            },
            new() { Name = "ask_client", Description = "Asks the client a question using sampling" }));

        int samplingCallCount = 0;
        TestChatClient testChatClient = new((messages, options, ct) =>
        {
            int currentCall = samplingCallCount++;
            var lastMessage = messages.LastOrDefault();

            // First call: Return a tool call request for get_weather
            if (currentCall == 0)
            {
                return Task.FromResult<ChatResponse>(new([
                    new ChatMessage(ChatRole.User, messages.First().Contents),
                    new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call_weather_123", "get_weather", new Dictionary<string, object?> { ["location"] = "Paris" })])
                ])
                {
                    ModelId = "test-model",
                    FinishReason = ChatFinishReason.ToolCalls
                });
            }
            // Second call (after tool result): Return final text response
            else
            {
                var toolResult = lastMessage?.Contents.OfType<FunctionResultContent>().FirstOrDefault();
                Assert.NotNull(toolResult);
                Assert.Equal("call_weather_123", toolResult.CallId);

                string resultText = toolResult.Result?.ToString() ?? string.Empty;
                Assert.Contains("Weather in Paris: sunny", resultText);

                return Task.FromResult<ChatResponse>(new([
                    new ChatMessage(ChatRole.User, messages.First().Contents),
                    new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call_weather_123", "get_weather", new Dictionary<string, object?> { ["location"] = "Paris" })]),
                    new ChatMessage(ChatRole.User, [toolResult]),
                    new ChatMessage(ChatRole.Assistant, [new TextContent($"Based on the weather data: {resultText}")])
                ])
                {
                    ModelId = "test-model",
                    FinishReason = ChatFinishReason.Stop
                });
            }
        });

        await using McpClient client = await CreateMcpClientForServer(new()
        {
            Handlers = new() { SamplingHandler = testChatClient.CreateSamplingHandler() },
        });

        var result = await client.CallToolAsync(
            "ask_client",
            new Dictionary<string, object?> { ["query"] = "What's the weather in Paris?" },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Null(result.IsError);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.NotNull(textContent);
        Assert.Contains("Weather in Paris: sunny, 22", textContent.Text);
        Assert.Equal(1, getWeatherToolCallCount);
        Assert.Equal(1, askClientToolCallCount);
        Assert.Equal(2, samplingCallCount);
    }

    /// <summary>Simple test IChatClient implementation for testing.</summary>
    private sealed class TestChatClient(Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> getResponse) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            getResponse(messages, options, cancellationToken);

        async IAsyncEnumerable<ChatResponseUpdate> IChatClient.GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            foreach (var update in response.ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        object? IChatClient.GetService(Type serviceType, object? serviceKey) => null;
        void IDisposable.Dispose() { }
    }

    [Fact]
    public async Task ListToolsAsync_WithRequestParams_ReturnsTools()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await client.ListToolsAsync(new ListToolsRequestParams(), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(12, result.Tools.Count);
        Assert.Contains(result.Tools, t => t.Name == "Method4");
    }

    [Fact]
    public async Task ListToolsAsync_WithRequestParams_NullThrows()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<ArgumentNullException>("requestParams",
            () => client.ListToolsAsync((ListToolsRequestParams)null!, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task CallToolAsync_WithRequestParams_ExecutesTool()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "Method4",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["i"] = JsonSerializer.SerializeToElement(42, McpJsonUtilities.DefaultOptions)
                }
            },
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Contains("Method4 Result 42", result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text);
    }

    [Fact]
    public async Task CallToolAsync_WithRequestParams_NullThrows()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<ArgumentNullException>("requestParams",
            () => client.CallToolAsync((CallToolRequestParams)null!, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task SetLoggingLevelAsync_WithRequestParams_SetsLevel()
    {
        await using McpClient client = await CreateMcpClientForServer();

        // Should not throw
        await client.SetLoggingLevelAsync(
            new SetLevelRequestParams { Level = LoggingLevel.Warning },
            TestContext.Current.CancellationToken);

        // Wait a bit for the server to process
        DateTime start = DateTime.UtcNow;
        while (Server.LoggingLevel is null)
        {
            await Task.Delay(1, TestContext.Current.CancellationToken);
            Assert.True(DateTime.UtcNow - start < TimeSpan.FromSeconds(10), "Timed out waiting for logging level to be set");
        }

        Assert.Equal(LoggingLevel.Warning, Server.LoggingLevel);
    }

    [Fact]
    public async Task SetLoggingLevelAsync_WithRequestParams_NullThrows()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<ArgumentNullException>("requestParams",
            () => client.SetLoggingLevelAsync((SetLevelRequestParams)null!, TestContext.Current.CancellationToken));
    }
}
