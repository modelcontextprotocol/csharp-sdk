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
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities(), TestContext.Current.CancellationToken);

        var action = async () => await server.SampleAsync(
            new CreateMessageRequestParams { Messages = [], MaxTokens = 1000 }, 
            CancellationToken.None);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(action);

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task SampleAsync_Should_SendRequest()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities { Sampling = new SamplingCapability() }, TestContext.Current.CancellationToken);

        // Act
        var result = await server.SampleAsync(
            new CreateMessageRequestParams { Messages = [], MaxTokens = 1000 }, 
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotEmpty(transport.SentMessages);
        // First message is the initialize response, second is the sampling request
        Assert.True(transport.SentMessages.Count >= 2, "Expected at least 2 messages (initialize response and sampling request)");
        var samplingRequest = Assert.IsType<JsonRpcRequest>(transport.SentMessages[1]);
        Assert.Equal(RequestMethods.SamplingCreateMessage, samplingRequest.Method);

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task RequestRootsAsync_Should_Throw_Exception_If_Client_Does_Not_Support_Roots()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities(), TestContext.Current.CancellationToken);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await server.RequestRootsAsync(
            new ListRootsRequestParams(), 
            CancellationToken.None));

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task RequestRootsAsync_Should_SendRequest()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities { Roots = new RootsCapability() }, TestContext.Current.CancellationToken);

        // Act
        var result = await server.RequestRootsAsync(new ListRootsRequestParams(), CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(transport.SentMessages);
        // First message is the initialize response, second is the roots request
        Assert.True(transport.SentMessages.Count >= 2, "Expected at least 2 messages (initialize response and roots request)");
        var rootsRequest = Assert.IsType<JsonRpcRequest>(transport.SentMessages[1]);
        Assert.Equal(RequestMethods.RootsList, rootsRequest.Method);

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task ElicitAsync_Should_Throw_Exception_If_Client_Does_Not_Support_Elicitation()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities(), TestContext.Current.CancellationToken);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await server.ElicitAsync(
            new ElicitRequestParams { Message = "" }, 
            CancellationToken.None));

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task ElicitAsync_Should_SendRequest()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities
        {
            Elicitation = new()
            {
                Form = new(),
            },
        }, TestContext.Current.CancellationToken);

        // Act
        var result = await server.ElicitAsync(new ElicitRequestParams { Message = "", RequestedSchema = new() }, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(transport.SentMessages);
        // First message is the initialize response, second is the elicit request
        Assert.True(transport.SentMessages.Count >= 2, "Expected at least 2 messages (initialize response and elicit request)");
        var elicitRequest = Assert.IsType<JsonRpcRequest>(transport.SentMessages[1]);
        Assert.Equal(RequestMethods.ElicitationCreate, elicitRequest.Method);

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
            new ServerCapabilities
            {
                Completions = new()
            },
            method: RequestMethods.CompletionComplete,
            configureOptions: options =>
            {
                options.Handlers.CompleteHandler = async (request, ct) =>
                    new CompleteResult
                    {
                        Completion = new()
                        {
                            Values = ["test"],
                            Total = 2,
                            HasMore = true
                        }
                    };
            },
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
            },
            RequestMethods.ResourcesTemplatesList,
            configureOptions: options =>
            {
                options.Handlers.ListResourceTemplatesHandler = async (request, ct) =>
                {
                    return new ListResourceTemplatesResult
                    {
                        ResourceTemplates = [new() { UriTemplate = "test", Name = "Test Resource" }]
                    };
                };
                options.Handlers.ListResourcesHandler = async (request, ct) =>
                {
                    return new ListResourcesResult
                    {
                        Resources = [new() { Uri = "test", Name = "Test Resource" }]
                    };
                };
                options.Handlers.ReadResourceHandler = (request, ct) => throw new NotImplementedException();
            },
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
            },
            RequestMethods.ResourcesList,
            configureOptions: options =>
            {
                options.Handlers.ListResourcesHandler = async (request, ct) =>
                {
                    return new ListResourcesResult
                    {
                        Resources = [new() { Uri = "test", Name = "Test Resource" }]
                    };
                };
                options.Handlers.ReadResourceHandler = (request, ct) => throw new NotImplementedException();
            },
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
            },
            method: RequestMethods.ResourcesRead,
            configureOptions: options =>
            {
                options.Handlers.ReadResourceHandler = async (request, ct) =>
                {
                    return new ReadResourceResult
                    {
                        Contents = [new TextResourceContents { Text = "test", Uri = "" }]
                    };
                };
                options.Handlers.ListResourcesHandler = (request, ct) => throw new NotImplementedException();
            },
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
            },
            method: RequestMethods.PromptsList,
            configureOptions: options =>
            {
                options.Handlers.ListPromptsHandler = async (request, ct) =>
                {
                    return new ListPromptsResult
                    {
                        Prompts = [new() { Name = "test" }]
                    };
                };
                options.Handlers.GetPromptHandler = (request, ct) => throw new NotImplementedException();
            },
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
            },
            method: RequestMethods.PromptsGet,
            configureOptions: options =>
            {
                options.Handlers.GetPromptHandler = async (request, ct) => new GetPromptResult { Description = "test" };
                options.Handlers.ListPromptsHandler = (request, ct) => throw new NotImplementedException();
            },
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
            },
            method: RequestMethods.ToolsList,
            configureOptions: options =>
            {
                options.Handlers.ListToolsHandler = async (request, ct) =>
                {
                    return new ListToolsResult
                    {
                        Tools = [new() { Name = "test" }]
                    };
                };
                options.Handlers.CallToolHandler = (request, ct) => throw new NotImplementedException();
            },
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
            },
            method: RequestMethods.ToolsCall,
            configureOptions: options =>
            {
                options.Handlers.CallToolHandler = async (request, ct) =>
                {
                    return new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = "test" }]
                    };
                };
                options.Handlers.ListToolsHandler = (request, ct) => throw new NotImplementedException();
            },
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

    [Fact]
    public async Task Can_Handle_Call_Tool_Requests_With_McpException()
    {
        const string errorMessage = "Tool execution failed with detailed error";
        await Can_Handle_Requests(
            new ServerCapabilities
            {
                Tools = new()
            },
            method: RequestMethods.ToolsCall,
            configureOptions: options =>
            {
                options.Handlers.CallToolHandler = async (request, ct) =>
                {
                    throw new McpException(errorMessage);
                };
                options.Handlers.ListToolsHandler = (request, ct) => throw new NotImplementedException();
            },
            assertResult: (_, response) =>
            {
                var result = JsonSerializer.Deserialize<CallToolResult>(response, McpJsonUtilities.DefaultOptions);
                Assert.NotNull(result);
                Assert.True(result.IsError);
                Assert.NotEmpty(result.Content);
                var textContent = Assert.IsType<TextContentBlock>(result.Content[0]);
                Assert.Contains(errorMessage, textContent.Text);
            });
    }

    [Fact]
    public async Task Can_Handle_Call_Tool_Requests_With_Plain_Exception()
    {
        await Can_Handle_Requests(
            new ServerCapabilities
            {
                Tools = new()
            },
            method: RequestMethods.ToolsCall,
            configureOptions: options =>
            {
                options.Handlers.CallToolHandler = async (request, ct) =>
                {
                    throw new InvalidOperationException("This sensitive message should not be exposed");
                };
                options.Handlers.ListToolsHandler = (request, ct) => throw new NotImplementedException();
            },
            assertResult: (_, response) =>
            {
                var result = JsonSerializer.Deserialize<CallToolResult>(response, McpJsonUtilities.DefaultOptions);
                Assert.NotNull(result);
                Assert.True(result.IsError);
                Assert.NotEmpty(result.Content);
                var textContent = Assert.IsType<TextContentBlock>(result.Content[0]);
                // Should be a generic error message, not the actual exception message
                Assert.DoesNotContain("sensitive", textContent.Text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("An error occurred", textContent.Text);
            });
    }

    [Fact]
    public async Task Can_Handle_Call_Tool_Requests_With_InputValidationException()
    {
        // Test that input validation errors (like ArgumentException from JSON deserialization)
        // are returned as tool execution errors (IsError=true) rather than protocol errors, per SEP-1303.
        const string errorMessage = "Input validation failed: invalid date format";

        await Can_Handle_Requests(
            new ServerCapabilities
            {
                Tools = new()
            },
            method: RequestMethods.ToolsCall,
            configureOptions: options =>
            {
                options.Handlers.CallToolHandler = async (request, ct) =>
                {
                    // Simulate an input validation error (like what would happen with wrong argument types)
                    throw new ArgumentException(errorMessage);
                };
                options.Handlers.ListToolsHandler = (request, ct) => throw new NotImplementedException();
            },
            assertResult: (_, response) =>
            {
                var result = JsonSerializer.Deserialize<CallToolResult>(response, McpJsonUtilities.DefaultOptions);
                Assert.NotNull(result);
                Assert.True(result.IsError, "Input validation errors should be returned as tool execution errors (IsError=true), not protocol errors");
                Assert.NotEmpty(result.Content);
                var textContent = Assert.IsType<TextContentBlock>(result.Content[0]);
                // ArgumentException should result in a generic error message that doesn't expose the exception details
                Assert.DoesNotContain(errorMessage, textContent.Text);
                Assert.Contains("An error occurred", textContent.Text);
            });
    }

    [Fact]
    public async Task Can_Handle_Call_Tool_Requests_With_McpProtocolException()
    {
        const string errorMessage = "Invalid tool parameters";
        const McpErrorCode errorCode = McpErrorCode.InvalidParams;

        await using var transport = new TestServerTransport();
        var options = CreateOptions(new ServerCapabilities { Tools = new() });
        options.Handlers.CallToolHandler = async (request, ct) =>
        {
            throw new McpProtocolException(errorMessage, errorCode);
        };
        options.Handlers.ListToolsHandler = (request, ct) => throw new NotImplementedException();

        await using var server = McpServer.Create(transport, options, LoggerFactory);

        var runTask = server.RunAsync(TestContext.Current.CancellationToken);

        var receivedMessage = new TaskCompletionSource<JsonRpcError>();

        transport.OnMessageSent = (message) =>
        {
            if (message is JsonRpcError error && error.Id.ToString() == "55")
                receivedMessage.SetResult(error);
        };

        await transport.SendMessageAsync(
            new JsonRpcRequest
            {
                Method = RequestMethods.ToolsCall,
                Id = new RequestId(55)
            },
            TestContext.Current.CancellationToken
        );

        var error = await receivedMessage.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.NotNull(error.Error);
        Assert.Equal((int)errorCode, error.Error.Code);
        Assert.Equal(errorMessage, error.Error.Message);

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task Can_Handle_Call_Tool_Requests_With_McpProtocolException_And_Data()
    {
        const string ErrorMessage = "Resource not found";
        const McpErrorCode ErrorCode = McpErrorCode.ResourceNotFound;
        const string ResourceUri = "file:///path/to/resource";

        await using var transport = new TestServerTransport();
        var options = CreateOptions(new ServerCapabilities { Tools = new() });
        options.Handlers.CallToolHandler = async (request, ct) =>
        {
            throw new McpProtocolException(ErrorMessage, ErrorCode)
            {
                Data =
                {
                    { "uri", ResourceUri }
                }
            };
        };
        options.Handlers.ListToolsHandler = (request, ct) => throw new NotImplementedException();

        await using var server = McpServer.Create(transport, options, LoggerFactory);

        var runTask = server.RunAsync(TestContext.Current.CancellationToken);

        var receivedMessage = new TaskCompletionSource<JsonRpcError>();

        transport.OnMessageSent = (message) =>
        {
            if (message is JsonRpcError error && error.Id.ToString() == "55")
                receivedMessage.SetResult(error);
        };

        await transport.SendMessageAsync(
            new JsonRpcRequest
            {
                Method = RequestMethods.ToolsCall,
                Id = new RequestId(55)
            },
            TestContext.Current.CancellationToken
        );

        var error = await receivedMessage.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.NotNull(error.Error);
        Assert.Equal((int)ErrorCode, error.Error.Code);
        Assert.Equal(ErrorMessage, error.Error.Message);
        Assert.NotNull(error.Error.Data);

        // Verify the data contains the uri (values are now JsonElements after serialization)
        var dataDict = Assert.IsType<Dictionary<string, JsonElement>>(error.Error.Data);
        Assert.True(dataDict.ContainsKey("uri"));
        Assert.Equal(ResourceUri, dataDict["uri"].GetString());

        await transport.DisposeAsync();
        await runTask;
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

        var response = await receivedMessage.Task.WaitAsync(TestConstants.DefaultTimeout);
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

        Assert.Throws<InvalidOperationException>(() => server.AsSamplingChatClient());
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

    [Fact]
    public async Task Server_IgnoresCancellationNotificationForInitializeRequest()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using McpServer server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);

        // Set up to capture the initialize response
        var initializeRequest = new JsonRpcRequest
        {
            Id = new RequestId("init-cancel-test"),
            Method = RequestMethods.Initialize,
            Params = JsonSerializer.SerializeToNode(new InitializeRequestParams
            {
                ProtocolVersion = "2024-11-05",
                Capabilities = new ClientCapabilities(),
                ClientInfo = new Implementation { Name = "test-client", Version = "1.0.0" }
            }, McpJsonUtilities.DefaultOptions)
        };

        var initResponseTcs = new TaskCompletionSource<JsonRpcResponse>();
        transport.OnMessageSent = (message) =>
        {
            if (message is JsonRpcResponse response && response.Id == initializeRequest.Id)
            {
                initResponseTcs.TrySetResult(response);
            }
        };

        // Act: Send initialize request and immediately send a cancellation notification for it.
        // Per spec, "The initialize request MUST NOT be cancelled by clients", so the server
        // should ignore the cancellation and still complete the initialize request.
        await transport.SendClientMessageAsync(initializeRequest, TestContext.Current.CancellationToken);
        await transport.SendClientMessageAsync(new JsonRpcNotification
        {
            Method = NotificationMethods.CancelledNotification,
            Params = JsonSerializer.SerializeToNode(
                new CancelledNotificationParams { RequestId = initializeRequest.Id },
                McpJsonUtilities.DefaultOptions),
        }, TestContext.Current.CancellationToken);

        // Assert: The initialize response should still arrive (not cancelled)
        var response = await initResponseTcs.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        Assert.NotNull(response.Result);
        var initResult = JsonSerializer.Deserialize<InitializeResult>(response.Result, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(initResult);
        Assert.NotNull(initResult.ServerInfo);

        await transport.DisposeAsync();
        await runTask;
    }

    private static async Task InitializeServerAsync(TestServerTransport transport, ClientCapabilities capabilities, CancellationToken cancellationToken = default)
    {
        var initializeRequest = new JsonRpcRequest
        {
            Id = new RequestId("init-1"),
            Method = RequestMethods.Initialize,
            Params = JsonSerializer.SerializeToNode(new InitializeRequestParams
            {
                ProtocolVersion = "2024-11-05",
                Capabilities = capabilities,
                ClientInfo = new Implementation { Name = "test-client", Version = "1.0.0" }
            }, McpJsonUtilities.DefaultOptions)
        };

        var tcs = new TaskCompletionSource<bool>();
        transport.OnMessageSent = (message) =>
        {
            if (message is JsonRpcResponse response && response.Id == initializeRequest.Id)
            {
                tcs.TrySetResult(true);
            }
        };

        await transport.SendClientMessageAsync(initializeRequest, cancellationToken);

        // Wait for the initialize response to be sent
        await tcs.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);
    }

#pragma warning disable MCPEXP002
    private sealed class TestServerForIChatClient(bool supportsSampling) : McpServer
#pragma warning restore MCPEXP002
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
            Assert.Equal("I am going to France.", Assert.IsType<TextContentBlock>(Assert.Single(rp.Messages[0].Content)).Text);
            Assert.Equal("What is the most famous tower in Paris?", Assert.IsType<TextContentBlock>(Assert.Single(rp.Messages[1].Content)).Text);

            CreateMessageResult result = new()
            {
                Content = [new TextContentBlock { Text = "The Eiffel Tower." }],
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
        options.Handlers.NotificationHandlers =
            [new(NotificationMethods.ProgressNotification, (notification, cancellationToken) =>
            {
                notificationReceived.TrySetResult(notification);
                return default;
            })];

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

    [Fact]
    public async Task NotifyProgressAsync_WithRequestParams_SendsNotification()
    {
        await using TestServerTransport transport = new();
        var options = CreateOptions();

        var server = McpServer.Create(transport, options, LoggerFactory);

        Task serverTask = server.RunAsync(TestContext.Current.CancellationToken);

        var progressParams = new ProgressNotificationParams
        {
            ProgressToken = new("test-token"),
            Progress = new()
            {
                Progress = 25,
                Total = 100,
                Message = "Sending progress via params",
            },
        };

        await server.NotifyProgressAsync(progressParams, TestContext.Current.CancellationToken);

        // Verify the notification was sent
        var notification = Assert.IsType<JsonRpcNotification>(
            transport.SentMessages.FirstOrDefault(m => m is JsonRpcNotification n && n.Method == NotificationMethods.ProgressNotification));
        Assert.NotNull(notification);
        var sentProgress = JsonSerializer.Deserialize<ProgressNotificationParams>(notification.Params, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(sentProgress);
        Assert.Equal("test-token", sentProgress.ProgressToken.ToString());
        Assert.Equal(25, sentProgress.Progress.Progress);
        Assert.Equal(100, sentProgress.Progress.Total);
        Assert.Equal("Sending progress via params", sentProgress.Progress.Message);

        await server.DisposeAsync();
        await serverTask;
    }

    [Fact]
    public async Task NotifyProgressAsync_WithRequestParams_NullThrows()
    {
        await using TestServerTransport transport = new();
        var options = CreateOptions();

        await using var server = McpServer.Create(transport, options, LoggerFactory);

        await Assert.ThrowsAsync<ArgumentNullException>("requestParams",
            () => server.NotifyProgressAsync((ProgressNotificationParams)null!, TestContext.Current.CancellationToken));
    }
}
