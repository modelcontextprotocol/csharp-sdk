using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public class McpServerTests : LoggedTest
{
    private readonly McpServerOptions _options;

    public McpServerTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
#if !NET
        Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "https://github.com/modelcontextprotocol/csharp-sdk/issues/587");
#endif
        _options = CreateOptions();
    }

    private static McpServerOptions CreateOptions(ServerCapabilities? capabilities = null)
    {
        return new McpServerOptions
        {
            ProtocolVersion = "2024",
            InitializationTimeout = TimeSpan.FromSeconds(30),
            Capabilities = capabilities,
        };
    }

    [Fact]
    public async Task Create_Should_Initialize_With_Valid_Parameters()
    {
        // Arrange & Act
        await using var transport = new TestServerTransport();
        await using McpServer server = McpServer.Create(transport, _options, LoggerFactory);

        // Assert
        Assert.NotNull(server);
        Assert.Null(server.NegotiatedProtocolVersion);
    }

    [Fact]
    public void Create_Throws_For_Null_ServerTransport()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>("transport", () => McpServer.Create(null!, _options, LoggerFactory));
    }

    [Fact]
    public async Task Create_Throws_For_Null_Options()
    {
        // Arrange, Act & Assert
        await using var transport = new TestServerTransport();
        Assert.Throws<ArgumentNullException>("serverOptions", () => McpServer.Create(transport, null!, LoggerFactory));
    }

    [Fact]
    public async Task Constructor_Should_Initialize_With_Valid_Parameters()
    {
        // Arrange & Act
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);

        // Assert
        Assert.NotNull(server);
    }

    [Fact]
    public void Constructor_Throws_For_Null_Transport()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => McpServer.Create(null!, _options, LoggerFactory));
    }

    [Fact]
    public async Task Constructor_Throws_For_Null_Options()
    {
        // Arrange, Act & Assert
        await using var transport = new TestServerTransport();
        Assert.Throws<ArgumentNullException>(() => McpServer.Create(transport, null!, LoggerFactory));
    }

    [Fact]
    public async Task Constructor_Does_Not_Throw_For_Null_Logger()
    {
        // Arrange & Act
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, null);

        // Assert
        Assert.NotNull(server);
    }

    [Fact]
    public async Task Constructor_Does_Not_Throw_For_Null_ServiceProvider()
    {
        // Arrange & Act
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory, null);

        // Assert
        Assert.NotNull(server);
    }

    [Fact]
    public async Task RunAsync_Should_Throw_InvalidOperationException_If_Already_Running()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => server.RunAsync(TestContext.Current.CancellationToken));

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task SampleAsync_Should_Throw_Exception_If_Client_Does_Not_Support_Sampling()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        SetClientCapabilities(server, new ClientCapabilities());

        var action = async () => await server.SampleAsync(new CreateMessageRequestParams { Messages = [] }, CancellationToken.None);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(action);
    }

    [Fact]
    public async Task SampleAsync_Should_SendRequest()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        SetClientCapabilities(server, new ClientCapabilities { Sampling = new SamplingCapability() });

        var runTask = server.RunAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await server.SampleAsync(new CreateMessageRequestParams { Messages = [] }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotEmpty(transport.SentMessages);
        Assert.IsType<JsonRpcRequest>(transport.SentMessages[0]);
        Assert.Equal(RequestMethods.SamplingCreateMessage, ((JsonRpcRequest)transport.SentMessages[0]).Method);

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task RequestRootsAsync_Should_Throw_Exception_If_Client_Does_Not_Support_Roots()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        SetClientCapabilities(server, new ClientCapabilities());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await server.RequestRootsAsync(new ListRootsRequestParams(), CancellationToken.None));
    }

    [Fact]
    public async Task RequestRootsAsync_Should_SendRequest()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
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
    public async Task ElicitAsync_Should_Throw_Exception_If_Client_Does_Not_Support_Elicitation()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        SetClientCapabilities(server, new ClientCapabilities());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await server.ElicitAsync(new ElicitRequestParams(), CancellationToken.None));
    }

    [Fact]
    public async Task ElicitAsync_Should_SendRequest()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        SetClientCapabilities(server, new ClientCapabilities { Elicitation = new ElicitationCapability() });
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await server.ElicitAsync(new ElicitRequestParams(), CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(transport.SentMessages);
        Assert.IsType<JsonRpcRequest>(transport.SentMessages[0]);
        Assert.Equal(RequestMethods.ElicitationCreate, ((JsonRpcRequest)transport.SentMessages[0]).Method);

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
            assertResult: (_, response) =>
            {
                JsonObject jObj = Assert.IsType<JsonObject>(response);
                Assert.Empty(jObj);
            });
    }

    [Fact]
    public async Task Can_Handle_Initialize_Requests()
    {
        AssemblyName expectedAssemblyName = (Assembly.GetEntryAssembly() ?? typeof(McpServer).Assembly).GetName();
        await Can_Handle_Requests(
            serverCapabilities: null,
            method: RequestMethods.Initialize,
            configureOptions: null,
            assertResult: (server, response) =>
            {
                var result = JsonSerializer.Deserialize<InitializeResult>(response, McpJsonUtilities.DefaultOptions);
                Assert.NotNull(result);
                Assert.Equal(expectedAssemblyName.Name, result.ServerInfo.Name);
                Assert.Equal(expectedAssemblyName.Version?.ToString() ?? "1.0.0", result.ServerInfo.Version);
                Assert.Equal("2024", result.ProtocolVersion);
                Assert.Equal("2024", server.NegotiatedProtocolVersion);
            });
    }

    [Fact]
    public async Task Can_Handle_Completion_Requests()
    {
        await Can_Handle_Requests(
            new()
            {
                Completions = new()
                {
                    CompleteHandler = async (request, ct) =>
                        new CompleteResult
                        {
                            Completion = new()
                            {
                                Values = ["test"],
                                Total = 2,
                                HasMore = true
                            }
                        }
                }
            },
            method: RequestMethods.CompletionComplete,
            configureOptions: null,
            assertResult: (_, response) =>
            {
                var result = JsonSerializer.Deserialize<CompleteResult>(response, McpJsonUtilities.DefaultOptions);
                Assert.NotNull(result?.Completion);
                Assert.Equal(["test"], result.Completion.Values);
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
                    ListResourceTemplatesHandler = async (request, ct) =>
                    {
                        return new ListResourceTemplatesResult
                        {
                            ResourceTemplates = [new() { UriTemplate = "test", Name = "Test Resource" }]
                        };
                    },
                    ListResourcesHandler = async (request, ct) =>
                    {
                        return new ListResourcesResult
                        {
                            Resources = [new() { Uri = "test", Name = "Test Resource" }]
                        };
                    },
                    ReadResourceHandler = (request, ct) => throw new NotImplementedException(),
                }
            },
            RequestMethods.ResourcesTemplatesList,
            configureOptions: null,
            assertResult: (_, response) =>
            {
                var result = JsonSerializer.Deserialize<ListResourceTemplatesResult>(response, McpJsonUtilities.DefaultOptions);
                Assert.NotNull(result?.ResourceTemplates);
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
                    ListResourcesHandler = async (request, ct) =>
                    {
                        return new ListResourcesResult
                        {
                            Resources = [new() { Uri = "test", Name = "Test Resource" }]
                        };
                    },
                    ReadResourceHandler = (request, ct) => throw new NotImplementedException(),
                }
            },
            RequestMethods.ResourcesList,
            configureOptions: null,
            assertResult: (_, response) =>
            {
                var result = JsonSerializer.Deserialize<ListResourcesResult>(response, McpJsonUtilities.DefaultOptions);
                Assert.NotNull(result?.Resources);
                Assert.NotEmpty(result.Resources);
                Assert.Equal("test", result.Resources[0].Uri);
            });
    }

    [Fact]
    public async Task Can_Handle_Resources_List_Requests_Throws_Exception_If_No_Handler_Assigned()
    {
        await Succeeds_Even_If_No_Handler_Assigned(new ServerCapabilities { Resources = new() }, RequestMethods.ResourcesList, "ListResources handler not configured");
    }

    [Fact]
    public async Task Can_Handle_ResourcesRead_Requests()
    {
        await Can_Handle_Requests(
            new ServerCapabilities
            {
                Resources = new()
                {
                    ReadResourceHandler = async (request, ct) =>
                    {
                        return new ReadResourceResult
                        {
                            Contents = [new TextResourceContents { Text = "test" }]
                        };
                    },
                    ListResourcesHandler = (request, ct) => throw new NotImplementedException(),
                }
            }, 
            method: RequestMethods.ResourcesRead,
            configureOptions: null,
            assertResult: (_, response) =>
            {
                var result = JsonSerializer.Deserialize<ReadResourceResult>(response, McpJsonUtilities.DefaultOptions);
                Assert.NotNull(result?.Contents);
                Assert.NotEmpty(result.Contents);

                TextResourceContents textResource = Assert.IsType<TextResourceContents>(result.Contents[0]);
                Assert.Equal("test", textResource.Text);
            });
    }

    [Fact]
    public async Task Can_Handle_Resources_Read_Requests_Throws_Exception_If_No_Handler_Assigned()
    {
        await Succeeds_Even_If_No_Handler_Assigned(new ServerCapabilities { Resources = new() }, RequestMethods.ResourcesRead, "ReadResource handler not configured");
    }

    [Fact]
    public async Task Can_Handle_List_Prompts_Requests()
    {
        await Can_Handle_Requests(
            new ServerCapabilities
            {
                Prompts = new()
                {
                    ListPromptsHandler = async (request, ct) =>
                    {
                        return new ListPromptsResult
                        {
                            Prompts = [new() { Name = "test" }]
                        };
                    },
                    GetPromptHandler = (request, ct) => throw new NotImplementedException(),
                },
            },
            method: RequestMethods.PromptsList,
            configureOptions: null,
            assertResult: (_, response) =>
            {
                var result = JsonSerializer.Deserialize<ListPromptsResult>(response, McpJsonUtilities.DefaultOptions);
                Assert.NotNull(result?.Prompts);
                Assert.NotEmpty(result.Prompts);
                Assert.Equal("test", result.Prompts[0].Name);
            });
    }

    [Fact]
    public async Task Can_Handle_List_Prompts_Requests_Throws_Exception_If_No_Handler_Assigned()
    {
        await Succeeds_Even_If_No_Handler_Assigned(new ServerCapabilities { Prompts = new() }, RequestMethods.PromptsList, "ListPrompts handler not configured");
    }

    [Fact]
    public async Task Can_Handle_Get_Prompts_Requests()
    {
        await Can_Handle_Requests(
            new ServerCapabilities 
            {
                Prompts = new()
                {
                    GetPromptHandler = async (request, ct) => new GetPromptResult { Description = "test" },
                    ListPromptsHandler = (request, ct) => throw new NotImplementedException(),
                }
            },
            method: RequestMethods.PromptsGet,
            configureOptions: null,
            assertResult: (_, response) =>
            {
                var result = JsonSerializer.Deserialize<GetPromptResult>(response, McpJsonUtilities.DefaultOptions);
                Assert.NotNull(result);
                Assert.Equal("test", result.Description);
            });
    }

    [Fact]
    public async Task Can_Handle_Get_Prompts_Requests_Throws_Exception_If_No_Handler_Assigned()
    {
        await Succeeds_Even_If_No_Handler_Assigned(new ServerCapabilities { Prompts = new() }, RequestMethods.PromptsGet, "GetPrompt handler not configured");
    }

    [Fact]
    public async Task Can_Handle_List_Tools_Requests()
    {
        await Can_Handle_Requests(
            new ServerCapabilities 
            {
                Tools = new()
                {
                    ListToolsHandler = async (request, ct) =>
                    {
                        return new ListToolsResult
                        {
                            Tools = [new() { Name = "test" }]
                        };
                    },
                    CallToolHandler = (request, ct) => throw new NotImplementedException(),
                }
            },
            method: RequestMethods.ToolsList,
            configureOptions: null,
            assertResult: (_, response) =>
            {
                var result = JsonSerializer.Deserialize<ListToolsResult>(response, McpJsonUtilities.DefaultOptions);
                Assert.NotNull(result);
                Assert.NotEmpty(result.Tools);
                Assert.Equal("test", result.Tools[0].Name);
            });
    }

    [Fact]
    public async Task Can_Handle_List_Tools_Requests_Throws_Exception_If_No_Handler_Assigned()
    {
        await Succeeds_Even_If_No_Handler_Assigned(new ServerCapabilities { Tools = new() }, RequestMethods.ToolsList, "ListTools handler not configured");
    }

    [Fact]
    public async Task Can_Handle_Call_Tool_Requests()
    {
        await Can_Handle_Requests(
            new ServerCapabilities
            {
                Tools = new()
                {
                    CallToolHandler = async (request, ct) =>
                    {
                        return new CallToolResult
                        {
                            Content = [new TextContentBlock { Text = "test" }]
                        };
                    },
                    ListToolsHandler = (request, ct) => throw new NotImplementedException(),
                }
            }, 
            method: RequestMethods.ToolsCall,
            configureOptions: null,
            assertResult: (_, response) =>
            {
                var result = JsonSerializer.Deserialize<CallToolResult>(response, McpJsonUtilities.DefaultOptions);
                Assert.NotNull(result);
                Assert.NotEmpty(result.Content);
                Assert.Equal("test", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
            });
    }

    [Fact]
    public async Task Can_Handle_Call_Tool_Requests_Throws_Exception_If_No_Handler_Assigned()
    {
        await Succeeds_Even_If_No_Handler_Assigned(new ServerCapabilities { Tools = new() }, RequestMethods.ToolsCall, "CallTool handler not configured");
    }

    private async Task Can_Handle_Requests(ServerCapabilities? serverCapabilities, string method, Action<McpServerOptions>? configureOptions, Action<McpServer, JsonNode?> assertResult)
    {
        await using var transport = new TestServerTransport();
        var options = CreateOptions(serverCapabilities);
        configureOptions?.Invoke(options);

        await using var server = McpServer.Create(transport, options, LoggerFactory);

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

        assertResult(server, response.Result);

        await transport.DisposeAsync();
        await runTask;
    }

    private async Task Succeeds_Even_If_No_Handler_Assigned(ServerCapabilities serverCapabilities, string method, string expectedError)
    {
        await using var transport = new TestServerTransport();
        var options = CreateOptions(serverCapabilities);

        var server = McpServer.Create(transport, options, LoggerFactory);
        await server.DisposeAsync();
    }

    [Fact]
    public async Task AsSamplingChatClient_NoSamplingSupport_Throws()
    {
        await using var server = new TestServerForIChatClient(supportsSampling: false);

        Assert.Throws<InvalidOperationException>(server.AsSamplingChatClient);
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

    [Fact]
    public async Task Can_SendMessage_Before_RunAsync()
    {
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);

        var logNotification = new JsonRpcNotification
        {
            Method = NotificationMethods.LoggingMessageNotification
        };
        await server.SendMessageAsync(logNotification, TestContext.Current.CancellationToken);

        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await transport.DisposeAsync();
        await runTask;

        Assert.NotEmpty(transport.SentMessages);
        Assert.Same(logNotification, transport.SentMessages[0]);
    }

    private static void SetClientCapabilities(McpServer server, ClientCapabilities capabilities)
    {
        FieldInfo? field = server.GetType().GetField("_clientCapabilities", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field.SetValue(server, capabilities);
    }

    private sealed class TestServerForIChatClient(bool supportsSampling) : McpServer
    {
        public override ClientCapabilities? ClientCapabilities =>
            supportsSampling ? new ClientCapabilities { Sampling = new SamplingCapability() } :
            null;

        public override McpServerOptions ServerOptions => new();

        public override Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
        {
            CreateMessageRequestParams? rp = JsonSerializer.Deserialize<CreateMessageRequestParams>(request.Params, McpJsonUtilities.DefaultOptions);

            Assert.NotNull(rp);
            Assert.Equal(0.75f, rp.Temperature);
            Assert.Equal(42, rp.MaxTokens);
            Assert.Equal(["."], rp.StopSequences);
            Assert.Null(rp.IncludeContext);
            Assert.Null(rp.Metadata);
            Assert.Null(rp.ModelPreferences);

            Assert.Equal($"You are a helpful assistant.{Environment.NewLine}More system stuff.", rp.SystemPrompt);

            Assert.Equal(2, rp.Messages.Count);
            Assert.Equal("I am going to France.", Assert.IsType<TextContentBlock>(rp.Messages[0].Content).Text);
            Assert.Equal("What is the most famous tower in Paris?", Assert.IsType<TextContentBlock>(rp.Messages[1].Content).Text);

            CreateMessageResult result = new()
            {
                Content = new TextContentBlock { Text = "The Eiffel Tower." },
                Model = "amazingmodel",
                Role = Role.Assistant,
                StopReason = "endTurn",
            };

            return Task.FromResult(new JsonRpcResponse
            { 
                Id = new RequestId("0"),
                Result = JsonSerializer.SerializeToNode(result, McpJsonUtilities.DefaultOptions),
            });
        }

        public override ValueTask DisposeAsync() => default;

        public override string? SessionId => throw new NotImplementedException();
        public override string? NegotiatedProtocolVersion => throw new NotImplementedException();
        public override Implementation? ClientInfo => throw new NotImplementedException();
        public override IServiceProvider? Services => throw new NotImplementedException();
        public override LoggingLevel? LoggingLevel => throw new NotImplementedException();
        public override Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public override Task RunAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public override IAsyncDisposable RegisterNotificationHandler(string method, Func<JsonRpcNotification, CancellationToken, ValueTask> handler) =>
            throw new NotImplementedException();
    }

    [Fact]
    public async Task NotifyProgress_Should_Be_Handled()
    {
        await using TestServerTransport transport = new();
        var options = CreateOptions();

        var notificationReceived = new TaskCompletionSource<JsonRpcNotification>();
        options.Capabilities = new()
        {
            NotificationHandlers = [new(NotificationMethods.ProgressNotification, (notification, cancellationToken) =>
            {
                notificationReceived.TrySetResult(notification);
                return default;
            })],
        };

        var server = McpServer.Create(transport, options, LoggerFactory);

        Task serverTask = server.RunAsync(TestContext.Current.CancellationToken);

        await transport.SendMessageAsync(new JsonRpcNotification
        {
            Method = NotificationMethods.ProgressNotification,
            Params = JsonSerializer.SerializeToNode(new ProgressNotificationParams
            {
                ProgressToken = new("abc"),
                Progress = new()
                {
                    Progress = 50,
                    Total = 100,
                    Message = "Progress message",
                },
            }, McpJsonUtilities.DefaultOptions),
        }, TestContext.Current.CancellationToken);

        var notification = await notificationReceived.Task;
        var progress = JsonSerializer.Deserialize<ProgressNotificationParams>(notification.Params, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(progress);
        Assert.Equal("abc", progress.ProgressToken.ToString());
        Assert.Equal(50, progress.Progress.Progress);
        Assert.Equal(100, progress.Progress.Total);
        Assert.Equal("Progress message", progress.Progress.Message);

        await server.DisposeAsync();
        await serverTask;
    }
}
