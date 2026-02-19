using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using OpenAI;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Tests;

public partial class ClientIntegrationTests : LoggedTest, IClassFixture<ClientIntegrationTestFixture>
{
    private static readonly string? s_openAIKey = Environment.GetEnvironmentVariable("AI:OpenAI:ApiKey");

    public static bool NoOpenAIKeySet => string.IsNullOrWhiteSpace(s_openAIKey);

    private readonly ClientIntegrationTestFixture _fixture;

    public ClientIntegrationTests(ClientIntegrationTestFixture fixture, ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        _fixture = fixture;
        _fixture.Initialize(LoggerFactory);
    }

    public static IEnumerable<object[]> GetClients() =>
        ClientIntegrationTestFixture.ClientIds.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task ConnectAndPing_Stdio(string clientId)
    {
        // Arrange

        // Act
        await using var client = await _fixture.CreateClientAsync(clientId);
        await client.PingAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(client);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task Connect_ShouldProvideServerFields(string clientId)
    {
        // Arrange

        // Act
        await using var client = await _fixture.CreateClientAsync(clientId);

        // Assert
        Assert.NotNull(client.ServerCapabilities);
        Assert.NotNull(client.ServerInfo);
        Assert.NotNull(client.NegotiatedProtocolVersion);
        Assert.NotNull(client.ServerInstructions);

        Assert.Null(client.SessionId);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task ListTools_Stdio(string clientId)
    {
        // arrange

        // act
        await using var client = await _fixture.CreateClientAsync(clientId);
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.NotEmpty(tools);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task CallTool_Stdio_EchoServer(string clientId)
    {
        // arrange

        // act
        await using var client = await _fixture.CreateClientAsync(clientId);
        var result = await client.CallToolAsync(
            "echo",
            new Dictionary<string, object?>
            {
                ["message"] = "Hello MCP!"
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // assert
        Assert.NotNull(result);
        Assert.Null(result.IsError);
        var textContent = Assert.Single(result.Content.OfType<TextContentBlock>());
        Assert.Equal("Echo: Hello MCP!", textContent.Text);
    }

    [Fact]
    public async Task CallTool_Stdio_EchoSessionId_ReturnsEmpty()
    {
        // arrange

        // act
        await using var client = await _fixture.CreateClientAsync("test_server");
        var result = await client.CallToolAsync("echoSessionId", cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.NotNull(result);
        Assert.Null(result.IsError);
        var textContent = Assert.Single(result.Content.OfType<TextContentBlock>());
        Assert.Empty(textContent.Text);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task CallTool_Stdio_ViaAIFunction_EchoServer(string clientId)
    {
        // arrange

        // act
        await using var client = await _fixture.CreateClientAsync(clientId);
        var aiFunctions = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var echo = aiFunctions.Single(t => t.Name == "echo");
        var result = await echo.InvokeAsync(new() { ["message"] = "Hello MCP!" }, TestContext.Current.CancellationToken);

        // assert
        Assert.NotNull(result);
        Assert.Contains("Echo: Hello MCP!", result.ToString());
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task ListPrompts_Stdio(string clientId)
    {
        // arrange

        // act
        await using var client = await _fixture.CreateClientAsync(clientId);
        var prompts = await client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.NotEmpty(prompts);
        // We could add specific assertions for the known prompts
        Assert.Contains(prompts, p => p.Name == "simple-prompt");
        Assert.Contains(prompts, p => p.Name == "args-prompt");
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task GetPrompt_Stdio_SimplePrompt(string clientId)
    {
        // arrange

        // act
        await using var client = await _fixture.CreateClientAsync(clientId);
        var result = await client.GetPromptAsync("simple-prompt", null, cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Messages);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task GetPrompt_Stdio_ComplexPrompt(string clientId)
    {
        // arrange

        // act
        await using var client = await _fixture.CreateClientAsync(clientId);
        var arguments = new Dictionary<string, object?>
        {
            { "city", "Seattle" },
            { "state", "WA" }
        };
        var result = await client.GetPromptAsync("args-prompt", arguments, cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Messages);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task GetPrompt_NonExistent_ThrowsException(string clientId)
    {
        // arrange

        // act
        await using var client = await _fixture.CreateClientAsync(clientId);
        await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.GetPromptAsync("non_existent_prompt", null, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task ListResourceTemplates_Stdio(string clientId)
    {
        // arrange

        // act
        await using var client = await _fixture.CreateClientAsync(clientId);

        IList<McpClientResourceTemplate> allResourceTemplates = await client.ListResourceTemplatesAsync(cancellationToken: TestContext.Current.CancellationToken);

        // The server provides test resource templates
        Assert.NotEmpty(allResourceTemplates);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task ListResources_Stdio(string clientId)
    {
        // arrange

        // act
        await using var client = await _fixture.CreateClientAsync(clientId);

        IList<McpClientResource> allResources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);

        // The server provides test resources
        Assert.NotEmpty(allResources);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task ReadResource_Stdio_TextResource(string clientId)
    {
        // arrange

        // act
        await using var client = await _fixture.CreateClientAsync(clientId);
        // Get available resources and read one that is text
        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var textResource = resources.First(r => r.MimeType?.StartsWith("text/", StringComparison.Ordinal) is true);
        var result = await client.ReadResourceAsync(textResource.Uri, null, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.Contents);

        TextResourceContents textContent = Assert.IsType<TextResourceContents>(result.Contents[0]);
        Assert.NotNull(textContent.Text);
    }

    // The latest "everything" server only exposes text-based file resources in its resource list;
    // binary resources are available via resource templates but not in the listed resources.
    [Fact]
    public async Task ReadResource_Stdio_BinaryResource()
    {
        // arrange
        var clientId = "test_server";

        // act
        await using var client = await _fixture.CreateClientAsync(clientId);
        // Read a binary resource from the test server
        var result = await client.ReadResourceAsync("test://static/resource/2", null, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.Contents);

        BlobResourceContents blobResource = Assert.IsType<BlobResourceContents>(result.Contents[0]);
        Assert.False(blobResource.Blob.IsEmpty);
    }

    // Not supported by "everything" server version on npx
    [Fact]
    public async Task SubscribeResource_Stdio()
    {
        // arrange
        var clientId = "test_server";

        // act
        TaskCompletionSource<bool> tcs = new();
        await using var client = await _fixture.CreateClientAsync(clientId, new()
        {
            Handlers = new()
            {
                NotificationHandlers =
                [
                    new(NotificationMethods.ResourceUpdatedNotification, (notification, cancellationToken) =>
                    {
                        var notificationParams = JsonSerializer.Deserialize<ResourceUpdatedNotificationParams>(notification.Params, McpJsonUtilities.DefaultOptions);
                        tcs.TrySetResult(true);
                        return default;
                    })
                ]
            }
        });

        await client.SubscribeToResourceAsync("test://static/resource/1", null, TestContext.Current.CancellationToken);

        await tcs.Task;
    }

    // Not supported by "everything" server version on npx
    [Fact]
    public async Task UnsubscribeResource_Stdio()
    {
        // arrange
        var clientId = "test_server";

        // act
        TaskCompletionSource<bool> receivedNotification = new();
        await using var client = await _fixture.CreateClientAsync(clientId, new()
        {
            Handlers = new()
            {
                NotificationHandlers =
                [
                    new(NotificationMethods.ResourceUpdatedNotification, (notification, cancellationToken) =>
                    {
                        var notificationParams = JsonSerializer.Deserialize<ResourceUpdatedNotificationParams>(notification.Params, McpJsonUtilities.DefaultOptions);
                        receivedNotification.TrySetResult(true);
                        return default;
                    })
                ]
            }
        });
        await client.SubscribeToResourceAsync("test://static/resource/1", null, TestContext.Current.CancellationToken);

        // wait until we received a notification
        await receivedNotification.Task;

        // unsubscribe
        await client.UnsubscribeFromResourceAsync("test://static/resource/1", null, TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task Complete_Stdio_ResourceTemplateReference(string clientId)
    {
        // arrange

        // act
        await using var client = await _fixture.CreateClientAsync(clientId);
        var templates = await client.ListResourceTemplatesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var template = templates.First();
        var result = await client.CompleteAsync(
            new ResourceTemplateReference { Uri = template.UriTemplate },
            "resourceId", "1",
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.NotNull(result);
        Assert.Single(result.Completion.Values);
        Assert.Equal("1", result.Completion.Values[0]);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task Complete_Stdio_PromptReference(string clientId)
    {
        // arrange

        // act
        await using var client = await _fixture.CreateClientAsync(clientId);
        var result = await client.CompleteAsync(
            new PromptReference { Name = "completable-prompt" },
            argumentName: "department", argumentValue: "Eng",
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.NotNull(result);
        Assert.Single(result.Completion.Values);
        Assert.Equal("Engineering", result.Completion.Values[0]);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task Sampling_Stdio(string clientId)
    {
        // Set up the sampling handler
        int samplingHandlerCalls = 0;
        await using var client = await _fixture.CreateClientAsync(clientId, new()
        {
            Handlers = new()
            {
                SamplingHandler = async (_, _, _) =>
                {
                    samplingHandlerCalls++;
                    return new CreateMessageResult
                    {
                        Model = "test-model",
                        Role = Role.Assistant,
                        Content = [new TextContentBlock { Text = "Test response" }],
                    };
                }
            }
        });

        // Call the server's trigger-sampling-request tool which should trigger our sampling handler
        var result = await client.CallToolAsync(
            "trigger-sampling-request",
            new Dictionary<string, object?>
            {
                ["prompt"] = "Test prompt",
                ["maxTokens"] = 100
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.NotNull(result);
        var textContent = Assert.Single(result.Content.OfType<TextContentBlock>());
        Assert.False(string.IsNullOrEmpty(textContent.Text));
    }

    //[Theory]
    //[MemberData(nameof(GetClients))]
    //public async Task Roots_Stdio_EverythingServer(string clientId)
    //{
    //    var rootsHandlerCalls = 0;
    //    var testRoots = new List<Root>
    //    {
    //        new() { Uri = "file:///test/root1", Name = "Test Root 1" },
    //        new() { Uri = "file:///test/root2", Name = "Test Root 2" }
    //    };

    //    await using var client = await _fixture.Factory.GetClientAsync(clientId);

    //    // Set up the roots handler
    //    client.SetRootsHandler((request, ct) =>
    //    {
    //        rootsHandlerCalls++;
    //        return Task.FromResult(new ListRootsResult
    //        {
    //            Roots = testRoots
    //        });
    //    });

    //    // Connect
    //    await client.ConnectAsync(TestContext.Current.CancellationToken);

    //    // assert
    //    // nothing to assert, no servers implement roots, so we if no exception is thrown, it's a success
    //    Assert.True(true);
    //}

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task Notifications_Stdio(string clientId)
    {
        await using var client = await _fixture.CreateClientAsync(clientId);

        // Verify we can send notifications without errors
        await client.SendNotificationAsync(NotificationMethods.RootsListChangedNotification, cancellationToken: TestContext.Current.CancellationToken);
        await client.SendNotificationAsync("test/notification", new TestNotification { Test = true }, cancellationToken: TestContext.Current.CancellationToken, serializerOptions: JsonContext3.Default.Options);

        // assert
        // no response to check, if no exception is thrown, it's a success
        Assert.True(true);
    }

    class TestNotification
    {
        public required bool Test { get; set; }
    }

    [Fact]
    public async Task CallTool_Stdio_MemoryServer()
    {
        // arrange
        StdioClientTransportOptions stdioOptions = new()
        {
            Command = "npx",
            Arguments = ["-y", "@modelcontextprotocol/server-memory"],
            Name = "memory",
        };

        McpClientOptions clientOptions = new()
        {
            ClientInfo = new() { Name = "IntegrationTestClient", Version = "1.0.0" }
        };

        await using var client = await McpClient.CreateAsync(
            new StdioClientTransport(stdioOptions),
            clientOptions,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        // act
        var result = await client.CallToolAsync(
            "read_graph",
            new Dictionary<string, object?>(),
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.NotNull(result);
        Assert.Null(result.IsError);
        Assert.Single(result.Content, c => c.Type == "text");

        await client.DisposeAsync();
    }

    [Fact(Skip = "Requires OpenAI API Key", SkipWhen = nameof(NoOpenAIKeySet))]
    public async Task ListToolsAsync_UsingEverythingServer_ToolsAreProperlyCalled()
    {
        // Get the MCP client and tools from it.
        await using var client = await McpClient.CreateAsync(
            new StdioClientTransport(_fixture.EverythingServerTransportOptions),
            cancellationToken: TestContext.Current.CancellationToken);
        var mappedTools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Create the chat client.
        using IChatClient chatClient = new OpenAIClient(s_openAIKey).GetChatClient("gpt-4o-mini").AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();

        // Create the messages.
        List<ChatMessage> messages = [new(ChatRole.System, "You are a helpful assistant.")];
        if (client.ServerInstructions is not null)
        {
            messages.Add(new(ChatRole.System, client.ServerInstructions));
        }
        messages.Add(new(ChatRole.User, "Please call the echo tool with the string 'Hello MCP!' and output the response ad verbatim."));

        // Call the chat client
        var response = await chatClient.GetResponseAsync(messages, new() { Tools = [.. mappedTools], Temperature = 0 }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Echo: Hello MCP!", response.Text);
    }

    [Fact(Skip = "Requires OpenAI API Key", SkipWhen = nameof(NoOpenAIKeySet))]
    public async Task SamplingViaChatClient_RequestResponseProperlyPropagated()
    {
        var samplingHandler = new OpenAIClient(s_openAIKey).GetChatClient("gpt-4o-mini")
            .AsIChatClient()
            .CreateSamplingHandler();
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(_fixture.EverythingServerTransportOptions), new()
        {
            Handlers = new()
            {
                SamplingHandler = samplingHandler
            }
        }, cancellationToken: TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync("trigger-sampling-request", new Dictionary<string, object?>()
        {
            ["prompt"] = "In just a few words, what is the most famous tower in Paris?",
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Content);
        var content = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Contains("LLM sampling result:", content.Text);
        Assert.Contains("Eiffel", content.Text);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task SetLoggingLevel_ReceivesLoggingMessages(string clientId)
    {
        TaskCompletionSource<bool> receivedNotification = new();
        await using var client = await _fixture.CreateClientAsync(clientId, new()
        {
            Handlers = new()
            {
                NotificationHandlers =
                [
                    new(NotificationMethods.LoggingMessageNotification, (notification, cancellationToken) =>
                    {
                        var loggingMessageNotificationParameters = JsonSerializer.Deserialize<LoggingMessageNotificationParams>(notification.Params, McpJsonUtilities.DefaultOptions);
                        if (loggingMessageNotificationParameters is not null)
                        {
                            receivedNotification.TrySetResult(true);
                        }
                        return default;
                    })
                ]
            }
        });

        // act
        await client.SetLoggingLevelAsync(LoggingLevel.Debug, options: null, TestContext.Current.CancellationToken);

        if (clientId == "everything")
        {
            // The everything server requires calling the toggle-simulated-logging tool to start sending log messages
            await client.CallToolAsync("toggle-simulated-logging", new Dictionary<string, object?>(), cancellationToken: TestContext.Current.CancellationToken);
        }

        // assert
        await receivedNotification.Task;
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task ListToolsAsync_WithRequestParams_ReturnsRawResult(string clientId)
    {
        await using var client = await _fixture.CreateClientAsync(clientId);

        var result = await client.ListToolsAsync(new ListToolsRequestParams(), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Tools);
        Assert.Contains(result.Tools, t => t.Name == "echo");
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task ListPromptsAsync_WithRequestParams_ReturnsRawResult(string clientId)
    {
        await using var client = await _fixture.CreateClientAsync(clientId);

        var result = await client.ListPromptsAsync(new ListPromptsRequestParams(), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Prompts);
        Assert.Contains(result.Prompts, p => p.Name == "simple-prompt");
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task GetPromptAsync_WithRequestParams_ReturnsRawResult(string clientId)
    {
        await using var client = await _fixture.CreateClientAsync(clientId);

        var result = await client.GetPromptAsync(
            new GetPromptRequestParams { Name = "simple-prompt" },
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Messages);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task ListResourceTemplatesAsync_WithRequestParams_ReturnsRawResult(string clientId)
    {
        await using var client = await _fixture.CreateClientAsync(clientId);

        var result = await client.ListResourceTemplatesAsync(
            new ListResourceTemplatesRequestParams(),
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotEmpty(result.ResourceTemplates);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task ListResourcesAsync_WithRequestParams_ReturnsRawResult(string clientId)
    {
        await using var client = await _fixture.CreateClientAsync(clientId);

        var result = await client.ListResourcesAsync(
            new ListResourcesRequestParams(),
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        // Low-level API returns only one page; the server provides resources but paginates
        Assert.NotEmpty(result.Resources);
        Assert.True(result.Resources.Count <= 100);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task ReadResourceAsync_WithRequestParams_ReturnsRawResult(string clientId)
    {
        await using var client = await _fixture.CreateClientAsync(clientId);

        // Get available resources and read the first one
        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var resource = resources.First();
        var result = await client.ReadResourceAsync(
            new ReadResourceRequestParams { Uri = resource.Uri },
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.Contents);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task CompleteAsync_WithRequestParams_ReturnsRawResult(string clientId)
    {
        await using var client = await _fixture.CreateClientAsync(clientId);

        var result = await client.CompleteAsync(
            new CompleteRequestParams
            {
                Ref = new PromptReference { Name = "completable-prompt" },
                Argument = new Argument { Name = "department", Value = "Eng" }
            },
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.Completion.Values);
        Assert.Equal("Engineering", result.Completion.Values[0]);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task CallToolAsync_WithRequestParams_ReturnsRawResult(string clientId)
    {
        await using var client = await _fixture.CreateClientAsync(clientId);

        var result = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "echo",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["message"] = JsonSerializer.SerializeToElement("Hello from RequestParams!", McpJsonUtilities.DefaultOptions)
                }
            },
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Null(result.IsError);
        var textContent = Assert.Single(result.Content.OfType<TextContentBlock>());
        Assert.Equal("Echo: Hello from RequestParams!", textContent.Text);
    }

    // Not supported by "everything" server version on npx
    [Fact]
    public async Task SubscribeToResourceAsync_WithRequestParams_Succeeds()
    {
        var clientId = "test_server";

        TaskCompletionSource<bool> tcs = new();
        await using var client = await _fixture.CreateClientAsync(clientId, new()
        {
            Handlers = new()
            {
                NotificationHandlers =
                [
                    new(NotificationMethods.ResourceUpdatedNotification, (notification, cancellationToken) =>
                    {
                        tcs.TrySetResult(true);
                        return default;
                    })
                ]
            }
        });

        await client.SubscribeToResourceAsync(
            new SubscribeRequestParams { Uri = "test://static/resource/1" },
            TestContext.Current.CancellationToken);

        await tcs.Task;
    }

    // Not supported by "everything" server version on npx
    [Fact]
    public async Task UnsubscribeFromResourceAsync_WithRequestParams_Succeeds()
    {
        var clientId = "test_server";

        TaskCompletionSource<bool> receivedNotification = new();
        await using var client = await _fixture.CreateClientAsync(clientId, new()
        {
            Handlers = new()
            {
                NotificationHandlers =
                [
                    new(NotificationMethods.ResourceUpdatedNotification, (notification, cancellationToken) =>
                    {
                        receivedNotification.TrySetResult(true);
                        return default;
                    })
                ]
            }
        });
        await client.SubscribeToResourceAsync(
            new SubscribeRequestParams { Uri = "test://static/resource/1" },
            TestContext.Current.CancellationToken);

        await receivedNotification.Task;

        await client.UnsubscribeFromResourceAsync(
            new UnsubscribeRequestParams { Uri = "test://static/resource/1" },
            TestContext.Current.CancellationToken);
    }

    [JsonSerializable(typeof(TestNotification))]
    partial class JsonContext3 : JsonSerializerContext;
}
