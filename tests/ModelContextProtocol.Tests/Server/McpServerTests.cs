using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using Moq;
using System.Reflection;

namespace ModelContextProtocol.Tests.Server;

public class McpServerTests : LoggedTest
{
    private readonly Mock<ITransport> _serverTransport;
    private readonly McpServerOptions _options;
    private readonly IServiceProvider _serviceProvider;

    public McpServerTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        _serverTransport = new Mock<ITransport>();
        _options = CreateOptions();
        _serviceProvider = new ServiceCollection().BuildServiceProvider();
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
        await using var server = McpServerFactory.Create(_serverTransport.Object, _options, LoggerFactory, _serviceProvider);

        // Assert
        Assert.NotNull(server);
    }

    [Fact]
    public void Constructor_Throws_For_Null_Transport()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => McpServerFactory.Create(null!, _options, LoggerFactory, _serviceProvider));
    }

    [Fact]
    public void Constructor_Throws_For_Null_Options()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => McpServerFactory.Create(_serverTransport.Object, null!, LoggerFactory, _serviceProvider));
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
        await using var server = McpServerFactory.Create(_serverTransport.Object, _options, LoggerFactory, null);

        // Assert
        Assert.NotNull(server);
    }

    [Fact]
    public async Task RunAsync_Should_Throw_InvalidOperationException_If_Already_Running()
    {
        // Arrange
        await using var server = McpServerFactory.Create(_serverTransport.Object, _options, LoggerFactory, _serviceProvider);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => server.RunAsync(TestContext.Current.CancellationToken));

        try
        {
            await runTask;
        }
        catch (NullReferenceException)
        {
            // _serverTransport.Object returns a null MessageReader
        }
    }

    [Fact]
    public async Task RequestSamplingAsync_Should_Throw_McpServerException_If_Client_Does_Not_Support_Sampling()
    {
        // Arrange
        await using var server = McpServerFactory.Create(_serverTransport.Object, _options, LoggerFactory, _serviceProvider);
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
        await using var server = McpServerFactory.Create(transport, _options, LoggerFactory, _serviceProvider);
        SetClientCapabilities(server, new ClientCapabilities { Sampling = new SamplingCapability() });

        var runTask = server.RunAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await server.RequestSamplingAsync(new CreateMessageRequestParams { Messages = [] }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotEmpty(transport.SentMessages);
        Assert.IsType<JsonRpcRequest>(transport.SentMessages[0]);
        Assert.Equal(RequestMethods.SamplingCreateMessage, ((JsonRpcRequest)transport.SentMessages[0]).Method);

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task RequestRootsAsync_Should_Throw_McpServerException_If_Client_Does_Not_Support_Roots()
    {
        // Arrange
        await using var server = McpServerFactory.Create(_serverTransport.Object, _options, LoggerFactory, _serviceProvider);
        SetClientCapabilities(server, new ClientCapabilities());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>("server", () => server.RequestRootsAsync(new ListRootsRequestParams(), CancellationToken.None));
    }

    [Fact]
    public async Task RequestRootsAsync_Should_SendRequest()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServerFactory.Create(transport, _options, LoggerFactory, _serviceProvider);
        SetClientCapabilities(server, new ClientCapabilities { Roots = new RootsCapability() });
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await server.RequestRootsAsync(new ListRootsRequestParams(), CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(transport.SentMessages);
        Assert.IsType<JsonRpcRequest>(transport.SentMessages[0]);
        Assert.Equal(RequestMethods.RootsList, ((JsonRpcRequest)transport.SentMessages[0]).Method);

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task Can_Handle_Ping_Requests()
    {
        await Can_Handle_Requests(
            serverCapabilities: null,
            method: RequestMethods.Ping,
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
            method: RequestMethods.Initialize,
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
            method: RequestMethods.CompletionComplete,
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
            method: RequestMethods.CompletionComplete,
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
            RequestMethods.ResourcesTemplatesList,
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
            RequestMethods.ResourcesList,
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
        await Throws_Exception_If_No_Handler_Assigned(new ServerCapabilities { Resources = new() }, RequestMethods.ResourcesList, "ListResources handler not configured");
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
                            Contents = [new TextResourceContents() { Text = "test" }]
                        });
                    },
                    ListResourcesHandler = (request, ct) => throw new NotImplementedException(),
                }
            }, 
            method: RequestMethods.ResourcesRead,
            configureOptions: null,
            assertResult: response =>
            {
                Assert.IsType<ReadResourceResult>(response);

                var result = (ReadResourceResult)response;
                Assert.NotNull(result.Contents);
                Assert.NotEmpty(result.Contents);

                TextResourceContents textResource = Assert.IsType<TextResourceContents>(result.Contents[0]);
                Assert.Equal("test", textResource.Text);
            });
    }

    [Fact]
    public async Task Can_Handle_Resources_Read_Requests_Throws_Exception_If_No_Handler_Assigned()
    {
        await Throws_Exception_If_No_Handler_Assigned(new ServerCapabilities { Resources = new() }, RequestMethods.ResourcesRead, "ReadResource handler not configured");
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
            method: RequestMethods.PromptsList,
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
        await Throws_Exception_If_No_Handler_Assigned(new ServerCapabilities { Prompts = new() }, RequestMethods.PromptsList, "ListPrompts handler not configured");
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
            method: RequestMethods.PromptsGet,
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
        await Throws_Exception_If_No_Handler_Assigned(new ServerCapabilities { Prompts = new() }, RequestMethods.PromptsGet, "GetPrompt handler not configured");
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
            method: RequestMethods.ToolsList,
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
        await Throws_Exception_If_No_Handler_Assigned(new ServerCapabilities { Tools = new() }, RequestMethods.ToolsList, "ListTools handler not configured");
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
            method: RequestMethods.ToolsCall,
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
        await Throws_Exception_If_No_Handler_Assigned(new ServerCapabilities { Tools = new() }, RequestMethods.ToolsCall, "CallTool handler not configured");
    }

    private async Task Can_Handle_Requests(ServerCapabilities? serverCapabilities, string method, Action<McpServerOptions>? configureOptions, Action<object> assertResult)
    {
        await using var transport = new TestServerTransport();
        var options = CreateOptions(serverCapabilities);
        configureOptions?.Invoke(options);

        await using var server = McpServerFactory.Create(transport, options, LoggerFactory, _serviceProvider);

        var runTask = server.RunAsync(TestContext.Current.CancellationToken);

        var receivedMessage = new TaskCompletionSource<JsonRpcResponse>();

        transport.OnMessageSent = (message) =>
        {
            if (message is JsonRpcResponse response && response.Id.ToString() == "55")
                receivedMessage.SetResult(response);
        };

        await transport.SendMessageAsync(
        new JsonRpcRequest
        {
            Method = method,
            Id = new RequestId(55)
        }
        );

        var response = await receivedMessage.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(response);
        Assert.NotNull(response.Result);

        assertResult(response.Result);

        await transport.DisposeAsync();
        await runTask;
    }

    private async Task Throws_Exception_If_No_Handler_Assigned(ServerCapabilities serverCapabilities, string method, string expectedError)
    {
        await using var transport = new TestServerTransport();
        var options = CreateOptions(serverCapabilities);

        Assert.Throws<McpServerException>(() => McpServerFactory.Create(transport, options, LoggerFactory, _serviceProvider));
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

        public Implementation? ClientInfo => throw new NotImplementedException();
        public McpServerOptions ServerOptions => throw new NotImplementedException();
        public IServiceProvider? Services => throw new NotImplementedException();
        public void AddNotificationHandler(string method, Func<JsonRpcNotification, Task> handler) => 
            throw new NotImplementedException();
        public Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task RunAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    [Fact]
    public async Task NotifyProgress_Should_Be_Handled()
    {
        var taskCompletionSource = new TaskCompletionSource();
        bool notificationHandled = false;
        await Notifications_Are_Handled(
            serverCapabilities: null,
            method: NotificationMethods.ProgressNotification,
            parameters: new ProgressNotification()
            {
                ProgressToken = new(),
                Progress = new()
                {
                    Progress = 50,
                    Total = 100,
                    Message = "Progress message",
                },
            },
            configureOptions: null,
            configureServer: server =>
            {
                server.AddNotificationHandler(NotificationMethods.ProgressNotification,
                    (notification) =>
                    {
                        notificationHandled = true;
                        var progress = (ProgressNotificationValue?)notification.Params;
                        Assert.NotNull(progress);
                        var progressValue = progress.Value;
                        taskCompletionSource.SetResult();
                        Assert.Equal(50, progressValue.Progress);
                        Assert.Equal(100, progressValue.Total);
                        Assert.Equal("Progress message", progressValue.Message);
                        return Task.CompletedTask;
                    });
            },
            assertResult: async response =>
            {
                //Note: awaiting here so handlers are guaranteed to be called first.
                await taskCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(1));
                Assert.True(notificationHandled);
            });
    }

    private async Task Notifications_Are_Handled(
        ServerCapabilities? serverCapabilities,
        string method, object? parameters,
        Action<McpServerOptions>? configureOptions,
        Action<IMcpServer>? configureServer,
        Action<JsonRpcNotification> assertResult)
    {
        await using TestServerTransport transport = new();
        var options = CreateOptions(serverCapabilities);
        configureOptions?.Invoke(options);

        await using var server = McpServerFactory.Create(
            transport, options, LoggerFactory, _serviceProvider);

        configureServer?.Invoke(server);
        await server.RunAsync();

        TaskCompletionSource<JsonRpcNotification> receivedMessage = new();

        transport.OnMessageSent = (message) =>
        {
            Assert.NotNull(message);
            if (message is JsonRpcNotification notification && notification.Method == method)
            {
                assertResult(notification);
                receivedMessage.SetResult(notification);
            }
        };

        await transport.SendMessageAsync(new JsonRpcNotification
        {
            Method = method,
            Params = parameters,
        });

        var response = await receivedMessage.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }
}
