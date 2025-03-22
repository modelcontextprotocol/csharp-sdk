using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using Moq;
using System.Reflection;

namespace ModelContextProtocol.Tests.Server;

public class McpServerTests
{
    private readonly Mock<IServerTransport> _serverTransport;
    private readonly Mock<ILoggerFactory> _loggerFactory;
    private readonly Mock<ILogger> _logger;
    private readonly McpServerOptions _options;
    private readonly IServiceProvider _serviceProvider;

    public McpServerTests()
    {
        _serverTransport = new Mock<IServerTransport>();
        _loggerFactory = new Mock<ILoggerFactory>();
        _logger = new Mock<ILogger>();
        _loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(_logger.Object);
        _options = CreateOptions();
        _serviceProvider = new Mock<IServiceProvider>().Object;
    }

    private static McpServerOptions CreateOptions(ServerCapabilities? capabilities = null)
    {
        return new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "TestServer", Version = "1.0" },
            ProtocolVersion = "2024",
            InitializationTimeout = TimeSpan.FromSeconds(30),
            Capabilities = capabilities,
        };
    }

    [Fact]
    public async Task Constructor_Should_Initialize_With_Valid_Parameters()
    {
        // Arrange & Act
        await using var server = McpServerFactory.Create(_serverTransport.Object, _options, _loggerFactory.Object, _serviceProvider);

        // Assert
        Assert.NotNull(server);
    }

    [Fact]
    public void Constructor_Throws_For_Null_Transport()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => McpServerFactory.Create(null!, _options, _loggerFactory.Object, _serviceProvider));
    }

    [Fact]
    public void Constructor_Throws_For_Null_Options()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => McpServerFactory.Create(_serverTransport.Object, null!, _loggerFactory.Object, _serviceProvider));
    }

    [Fact]
    public async Task Constructor_Does_Not_Throw_For_Null_Logger()
    {
        // Arrange & Act
        await using var server = McpServerFactory.Create(_serverTransport.Object, _options, null, _serviceProvider);

        // Assert
        Assert.NotNull(server);
    }

    [Fact]
    public async Task Constructor_Does_Not_Throw_For_Null_ServiceProvider()
    {
        // Arrange & Act
        await using var server = McpServerFactory.Create(_serverTransport.Object, _options, _loggerFactory.Object, null);

        // Assert
        Assert.NotNull(server);
    }

    [Fact]
    public async Task StartAsync_Should_Throw_InvalidOperationException_If_Already_Initializing()
    {
        // Arrange
        await using var server = McpServerFactory.Create(_serverTransport.Object, _options, _loggerFactory.Object, _serviceProvider);
        var task = server.StartAsync(TestContext.Current.CancellationToken);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => server.StartAsync(TestContext.Current.CancellationToken));

        await task;
    }

    [Fact]
    public async Task StartAsync_Should_Do_Nothing_If_Already_Initialized()
    {
        // Arrange
        await using var server = McpServerFactory.Create(_serverTransport.Object, _options, _loggerFactory.Object, _serviceProvider);
        SetInitialized(server, true);

        await server.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        _serverTransport.Verify(t => t.StartListeningAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_ShouldStartListening()
    {
        // Arrange
        await using var server = McpServerFactory.Create(_serverTransport.Object, _options, _loggerFactory.Object, _serviceProvider);

        // Act
        await server.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        _serverTransport.Verify(t => t.StartListeningAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_Sets_Initialized_After_Transport_Responses_Initialized_Notification()
    {
        await using var transport = new TestServerTransport();
        await using var server = McpServerFactory.Create(transport, _options, _loggerFactory.Object, _serviceProvider);

        await server.StartAsync(TestContext.Current.CancellationToken);

        // Send initialized notification
        await transport.SendMessageAsync(new JsonRpcNotification
            {
                Method = "notifications/initialized"
            }
, TestContext.Current.CancellationToken);

        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.True(server.IsInitialized);
    }

    [Fact]
    public async Task RequestSamplingAsync_Should_Throw_McpServerException_If_Client_Does_Not_Support_Sampling()
    {
        // Arrange
        await using var server = McpServerFactory.Create(_serverTransport.Object, _options, _loggerFactory.Object, _serviceProvider);
        SetClientCapabilities(server, new ClientCapabilities());

        var action = () => server.RequestSamplingAsync(new CreateMessageRequestParams { Messages = [] }, CancellationToken.None);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>("server", action);
    }

    [Fact]
    public async Task RequestSamplingAsync_Should_SendRequest()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServerFactory.Create(transport, _options, _loggerFactory.Object, _serviceProvider);
        SetClientCapabilities(server, new ClientCapabilities { Sampling = new SamplingCapability() });

        await server.StartAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await server.RequestSamplingAsync(new CreateMessageRequestParams { Messages = [] }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotEmpty(transport.SentMessages);
        Assert.IsType<JsonRpcRequest>(transport.SentMessages[0]);
        Assert.Equal("sampling/createMessage", ((JsonRpcRequest)transport.SentMessages[0]).Method);
    }

    [Fact]
    public async Task RequestRootsAsync_Should_Throw_McpServerException_If_Client_Does_Not_Support_Roots()
    {
        // Arrange
        await using var server = McpServerFactory.Create(_serverTransport.Object, _options, _loggerFactory.Object, _serviceProvider);
        SetClientCapabilities(server, new ClientCapabilities());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>("server", () => server.RequestRootsAsync(new ListRootsRequestParams(), CancellationToken.None));
    }

    [Fact]
    public async Task RequestRootsAsync_Should_SendRequest()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServerFactory.Create(transport, _options, _loggerFactory.Object, _serviceProvider);
        SetClientCapabilities(server, new ClientCapabilities { Roots = new RootsCapability() });
        await server.StartAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await server.RequestRootsAsync(new ListRootsRequestParams(), CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(transport.SentMessages);
        Assert.IsType<JsonRpcRequest>(transport.SentMessages[0]);
        Assert.Equal("roots/list", ((JsonRpcRequest)transport.SentMessages[0]).Method);
    }

    [Fact]
    public async Task Throws_Exception_If_Not_Connected()
    {
        await using var server = McpServerFactory.Create(_serverTransport.Object, _options, _loggerFactory.Object, _serviceProvider);
        SetClientCapabilities(server, new ClientCapabilities { Roots = new RootsCapability() });
        _serverTransport.SetupGet(t => t.IsConnected).Returns(false);

        var action = async () => await server.RequestRootsAsync(new ListRootsRequestParams(), CancellationToken.None);

        await Assert.ThrowsAsync<McpClientException>(action);
    }

    [Fact]
    public async Task Can_Handle_Ping_Requests()
    {
        await Can_Handle_Requests(
            serverCapabilities: null,
            method: "ping",
            configureOptions: null,
            assertResult: response =>
            {
                Assert.IsType<PingResult>(response);
            });
    }

    [Fact]
    public async Task Can_Handle_Initialize_Requests()
    {
        await Can_Handle_Requests(
            serverCapabilities: null,
            method: "initialize",
            configureOptions: null,
            assertResult: response =>
            {
                Assert.IsType<InitializeResult>(response);

                var result = (InitializeResult)response;
                Assert.Equal("TestServer", result.ServerInfo.Name);
                Assert.Equal("1.0", result.ServerInfo.Version);
                Assert.Equal("2024", result.ProtocolVersion);
            });
    }

    [Fact]
    public async Task Can_Handle_Completion_Requests()
    {
        await Can_Handle_Requests(
            serverCapabilities: null,
            method: "completion/complete",
            configureOptions: null,
            assertResult: response =>
            {
                Assert.IsType<CompleteResult>(response);

                var result = (CompleteResult)response;
                Assert.NotNull(result.Completion);
                Assert.Empty(result.Completion.Values);
                Assert.Equal(0, result.Completion.Total);
                Assert.False(result.Completion.HasMore);
            });
    }

    [Fact]
    public async Task Can_Handle_Completion_Requests_With_Handler()
    {
        await Can_Handle_Requests(
            serverCapabilities: null,
            method: "completion/complete",
            configureOptions: options =>
            {
                options.GetCompletionHandler = (request, ct) =>
                    Task.FromResult(new CompleteResult
                    {
                        Completion = new()
                        {
                            Values = ["test"],
                            Total = 2,
                            HasMore = true
                        }
                    });
            },
            assertResult: response =>
            {
                Assert.IsType<CompleteResult>(response);

                var result = (CompleteResult)response;
                Assert.NotNull(result.Completion);
                Assert.NotEmpty(result.Completion.Values);
                Assert.Equal("test", result.Completion.Values[0]);
                Assert.Equal(2, result.Completion.Total);
                Assert.True(result.Completion.HasMore);
            });
    }

    [Fact]
    public async Task Can_Handle_ResourceTemplates_List_Requests()
    {
        await Can_Handle_Requests(
            new ServerCapabilities
            {
                Resources = new()
                {
                    ListResourceTemplatesHandler = (request, ct) =>
                    {
                        return Task.FromResult(new ListResourceTemplatesResult
                        {
                            ResourceTemplates = [new() { UriTemplate = "test", Name = "Test Resource" }]
                        });
                    },
                    ListResourcesHandler = (request, ct) =>
                    {
                        return Task.FromResult(new ListResourcesResult
                        {
                            Resources = [new() { Uri = "test", Name = "Test Resource" }]
                        });
                    },
                    ReadResourceHandler = (request, ct) => throw new NotImplementedException(),
                }
            },
            "resources/templates/list",
            configureOptions: null,
            assertResult: response =>
            {
                Assert.IsType<ListResourceTemplatesResult>(response);

                var result = (ListResourceTemplatesResult)response;
                Assert.NotNull(result.ResourceTemplates);
                Assert.NotEmpty(result.ResourceTemplates);
                Assert.Equal("test", result.ResourceTemplates[0].UriTemplate);
            });
    }

    [Fact]
    public async Task Can_Handle_Resources_List_Requests()
    {
        await Can_Handle_Requests(
            new ServerCapabilities
            {
                Resources = new()
                {
                    ListResourcesHandler = (request, ct) =>
                    {
                        return Task.FromResult(new ListResourcesResult
                        {
                            Resources = [new() { Uri = "test", Name = "Test Resource" }]
                        });
                    },
                    ReadResourceHandler = (request, ct) => throw new NotImplementedException(),
                }
            },
            "resources/list",
            configureOptions: null,
            assertResult: response =>
            {
                Assert.IsType<ListResourcesResult>(response);

                var result = (ListResourcesResult)response;
                Assert.NotNull(result.Resources);
                Assert.NotEmpty(result.Resources);
                Assert.Equal("test", result.Resources[0].Uri);
            });
    }

    [Fact]
    public async Task Can_Handle_Resources_List_Requests_Throws_Exception_If_No_Handler_Assigned()
    {
        await Throws_Exception_If_No_Handler_Assigned(new ServerCapabilities { Resources = new() }, "resources/list", "ListResources handler not configured");
    }

    [Fact]
    public async Task Can_Handle_ResourcesRead_Requests()
    {
        await Can_Handle_Requests(
            new ServerCapabilities
            {
                Resources = new()
                {
                    ReadResourceHandler = (request, ct) =>
                    {
                        return Task.FromResult(new ReadResourceResult
                        {
                            Contents = [new() { Text = "test" }]
                        });
                    },
                    ListResourcesHandler = (request, ct) => throw new NotImplementedException(),
                }
            }, 
            method: "resources/read",
            configureOptions: null,
            assertResult: response =>
            {
                Assert.IsType<ReadResourceResult>(response);

                var result = (ReadResourceResult)response;
                Assert.NotNull(result.Contents);
                Assert.NotEmpty(result.Contents);
                Assert.Equal("test", result.Contents[0].Text);
            });
    }

    [Fact]
    public async Task Can_Handle_Resources_Read_Requests_Throws_Exception_If_No_Handler_Assigned()
    {
        await Throws_Exception_If_No_Handler_Assigned(new ServerCapabilities { Resources = new() }, "resources/read", "ReadResource handler not configured");
    }

    [Fact]
    public async Task Can_Handle_List_Prompts_Requests()
    {
        await Can_Handle_Requests(
            new ServerCapabilities
            {
                Prompts = new()
                {
                    ListPromptsHandler = (request, ct) =>
                    {
                        return Task.FromResult(new ListPromptsResult
                        {
                            Prompts = [new() { Name = "test" }]
                        });
                    },
                    GetPromptHandler = (request, ct) => throw new NotImplementedException(),
                },
            },
            method: "prompts/list",
            configureOptions: null,
            assertResult: response =>
            {
                Assert.IsType<ListPromptsResult>(response);

                var result = (ListPromptsResult)response;
                Assert.NotNull(result.Prompts);
                Assert.NotEmpty(result.Prompts);
                Assert.Equal("test", result.Prompts[0].Name);
            });
    }

    [Fact]
    public async Task Can_Handle_List_Prompts_Requests_Throws_Exception_If_No_Handler_Assigned()
    {
        await Throws_Exception_If_No_Handler_Assigned(new ServerCapabilities { Prompts = new() }, "prompts/list", "ListPrompts handler not configured");
    }

    [Fact]
    public async Task Can_Handle_Get_Prompts_Requests()
    {
        await Can_Handle_Requests(
            new ServerCapabilities 
            {
                Prompts = new()
                {
                    GetPromptHandler = (request, ct) => Task.FromResult(new GetPromptResult { Description = "test" }),
                    ListPromptsHandler = (request, ct) => throw new NotImplementedException(),
                }
            },
            method: "prompts/get",
            configureOptions: null,
            assertResult: response =>
            {
                Assert.IsType<GetPromptResult>(response);

                var result = (GetPromptResult)response;
                Assert.Equal("test", result.Description);
            });
    }

    [Fact]
    public async Task Can_Handle_Get_Prompts_Requests_Throws_Exception_If_No_Handler_Assigned()
    {
        await Throws_Exception_If_No_Handler_Assigned(new ServerCapabilities { Prompts = new() }, "prompts/get", "GetPrompt handler not configured");
    }

    [Fact]
    public async Task Can_Handle_List_Tools_Requests()
    {
        await Can_Handle_Requests(
            new ServerCapabilities 
            {
                Tools = new()
                {
                    ListToolsHandler = (request, ct) =>
                    {
                        return Task.FromResult(new ListToolsResult
                        {
                            Tools = [new() { Name = "test" }]
                        });
                    },
                    CallToolHandler = (request, ct) => throw new NotImplementedException(),
                }
            },
            method: "tools/list",
            configureOptions: null,
            assertResult: response =>
            {
                Assert.IsType<ListToolsResult>(response);

                var result = (ListToolsResult)response;
                Assert.NotEmpty(result.Tools);
                Assert.Equal("test", result.Tools[0].Name);
            });
    }

    [Fact]
    public async Task Can_Handle_List_Tools_Requests_Throws_Exception_If_No_Handler_Assigned()
    {
        await Throws_Exception_If_No_Handler_Assigned(new ServerCapabilities { Tools = new() }, "tools/list", "ListTools handler not configured");
    }

    [Fact]
    public async Task Can_Handle_Call_Tool_Requests()
    {
        await Can_Handle_Requests(
            new ServerCapabilities
            {
                Tools = new()
                {
                    CallToolHandler = (request, ct) =>
                    {
                        return Task.FromResult(new CallToolResponse
                        {
                            Content = [new Content { Text = "test" }]
                        });
                    },
                    ListToolsHandler = (request, ct) => throw new NotImplementedException(),
                }
            }, 
            method: "tools/call",
            configureOptions: null,
            assertResult: response =>
            {
                Assert.IsType<CallToolResponse>(response);

                var result = (CallToolResponse)response;
                Assert.NotEmpty(result.Content);
                Assert.Equal("test", result.Content[0].Text);
            });
    }

    [Fact]
    public async Task Can_Handle_Call_Tool_Requests_Throws_Exception_If_No_Handler_Assigned()
    {
        await Throws_Exception_If_No_Handler_Assigned(new ServerCapabilities { Tools = new() }, "tools/call", "CallTool handler not configured");
    }

    private async Task Can_Handle_Requests(ServerCapabilities? serverCapabilities, string method, Action<McpServerOptions>? configureOptions, Action<object> assertResult)
    {
        await using var transport = new TestServerTransport();
        var options = CreateOptions(serverCapabilities);
        configureOptions?.Invoke(options);

        await using var server = McpServerFactory.Create(transport, options, _loggerFactory.Object, _serviceProvider);

        await server.StartAsync();

        var receivedMessage = new TaskCompletionSource<JsonRpcResponse>();

        transport.OnMessageSent = (message) =>
        {
            if (message is JsonRpcResponse response && response.Id.AsNumber == 55)
                receivedMessage.SetResult(response);
        };

        await transport.SendMessageAsync(
        new JsonRpcRequest
        {
            Method = method,
            Id = RequestId.FromNumber(55)
        }
        );

        var response = await receivedMessage.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.NotNull(response);
        Assert.NotNull(response.Result);

        assertResult(response.Result);
    }

    private async Task Throws_Exception_If_No_Handler_Assigned(ServerCapabilities serverCapabilities, string method, string expectedError)
    {
        await using var transport = new TestServerTransport();
        var options = CreateOptions(serverCapabilities);

        Assert.Throws<McpServerException>(() => McpServerFactory.Create(transport, options, _loggerFactory.Object, _serviceProvider));
    }

    [Fact]
    public async Task AsSamplingChatClient_NoSamplingSupport_Throws()
    {
        await using var server = new TestServerForIChatClient(supportsSampling: false);

        Assert.Throws<ArgumentException>("server", () => server.AsSamplingChatClient());
    }


    [Fact]
    public async Task AsSamplingChatClient_HandlesRequestResponse()
    {
        await using var server = new TestServerForIChatClient(supportsSampling: true);

        IChatClient client = server.AsSamplingChatClient();

        ChatMessage[] messages =
        [
            new (ChatRole.System, "You are a helpful assistant."),
            new (ChatRole.User, "I am going to France."),
            new (ChatRole.User, "What is the most famous tower in Paris?"),
            new (ChatRole.System, "More system stuff."),
        ];

        ChatResponse response = await client.GetResponseAsync(messages, new ChatOptions
        {
            Temperature = 0.75f,
            MaxOutputTokens = 42,
            StopSequences = ["."],
        }, TestContext.Current.CancellationToken);

        Assert.Equal("amazingmodel", response.ModelId);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
        Assert.Single(response.Messages);
        Assert.Equal("The Eiffel Tower.", response.Text);
        Assert.Equal(ChatRole.Assistant, response.Messages[0].Role);
    }

    private static void SetClientCapabilities(IMcpServer server, ClientCapabilities capabilities)
    {
        PropertyInfo? property = server.GetType().GetProperty("ClientCapabilities", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        property.SetValue(server, capabilities);
    }

    private static void SetInitialized(IMcpServer server, bool isInitialized)
    {
        PropertyInfo? property = server.GetType().GetProperty("IsInitialized", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        property.SetValue(server, isInitialized);
    }

    private sealed class TestServerForIChatClient(bool supportsSampling) : IMcpServer
    {
        public ClientCapabilities? ClientCapabilities =>
            supportsSampling ? new ClientCapabilities { Sampling = new SamplingCapability() } :
            null;

        public Task<T> SendRequestAsync<T>(JsonRpcRequest request, CancellationToken cancellationToken) where T : class
        {
            CreateMessageRequestParams rp = Assert.IsType<CreateMessageRequestParams>(request.Params);

            Assert.Equal(0.75f, rp.Temperature);
            Assert.Equal(42, rp.MaxTokens);
            Assert.Equal(["."], rp.StopSequences);
            Assert.Null(rp.IncludeContext);
            Assert.Null(rp.Metadata);
            Assert.Null(rp.ModelPreferences);

            Assert.Equal($"You are a helpful assistant.{Environment.NewLine}More system stuff.", rp.SystemPrompt);

            Assert.Equal(2, rp.Messages.Count);
            Assert.Equal("I am going to France.", rp.Messages[0].Content.Text);
            Assert.Equal("What is the most famous tower in Paris?", rp.Messages[1].Content.Text);

            CreateMessageResult result = new()
            {
                Content = new() { Text = "The Eiffel Tower.", Type = "text" },
                Model = "amazingmodel",
                Role = "assistant",
                StopReason = "endTurn",
            };
            return Task.FromResult((T)(object)result);
        }

        public ValueTask DisposeAsync() => default;

        public bool IsInitialized => throw new NotImplementedException();

        public Implementation? ClientInfo => throw new NotImplementedException();
        public IServiceProvider? ServiceProvider => throw new NotImplementedException();
        public void AddNotificationHandler(string method, Func<JsonRpcNotification, Task> handler) => 
            throw new NotImplementedException();
        public Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task StartAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
