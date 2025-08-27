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
        await client.PingAsync(TestContext.Current.CancellationToken);

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

        if (clientId != "everything")   // Note: Comment the below assertion back when the everything server is updated to provide instructions
        {
            Assert.NotNull(client.ServerInstructions);
        }

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
        var prompts = await client.ListPromptsAsync(TestContext.Current.CancellationToken);

        // assert
        Assert.NotEmpty(prompts);
        // We could add specific assertions for the known prompts
        Assert.Contains(prompts, p => p.Name == "simple_prompt");
        Assert.Contains(prompts, p => p.Name == "complex_prompt");
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task GetPrompt_Stdio_SimplePrompt(string clientId)
    {
        // arrange

        // act
        await using var client = await _fixture.CreateClientAsync(clientId);
        var result = await client.GetPromptAsync("simple_prompt", null, cancellationToken: TestContext.Current.CancellationToken);

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
            { "temperature", "0.7" },
            { "style", "formal" }
        };
        var result = await client.GetPromptAsync("complex_prompt", arguments, cancellationToken: TestContext.Current.CancellationToken);

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
        await Assert.ThrowsAsync<McpException>(async () => 
            await client.GetPromptAsync("non_existent_prompt", null, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task ListResourceTemplates_Stdio(string clientId)
    {
        // arrange

        // act
        await using var client = await _fixture.CreateClientAsync(clientId);

        IList<McpClientResourceTemplate> allResourceTemplates = await client.ListResourceTemplatesAsync(TestContext.Current.CancellationToken);

        // The server provides a single test resource template
        Assert.Single(allResourceTemplates);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task ListResources_Stdio(string clientId)
    {
        // arrange

        // act
        await using var client = await _fixture.CreateClientAsync(clientId);

        IList<McpClientResource> allResources = await client.ListResourcesAsync(TestContext.Current.CancellationToken);

        // The server provides 100 test resources
        Assert.Equal(100, allResources.Count);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task ReadResource_Stdio_TextResource(string clientId)
    {
        // arrange

        // act
        await using var client = await _fixture.CreateClientAsync(clientId);
        // Odd numbered resources are text in the everything server (despite the docs saying otherwise)
        // 1 is index 0, which is "even" in the 0-based index
        var result = await client.ReadResourceAsync("test://static/resource/1", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.Contents);

        TextResourceContents textResource = Assert.IsType<TextResourceContents>(result.Contents[0]);
        Assert.NotNull(textResource.Text);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task ReadResource_Stdio_BinaryResource(string clientId)
    {
        // arrange

        // act
        await using var client = await _fixture.CreateClientAsync(clientId);
        // Even numbered resources are binary in the everything server (despite the docs saying otherwise)
        // 2 is index 1, which is "odd" in the 0-based index
        var result = await client.ReadResourceAsync("test://static/resource/2", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.Contents);

        BlobResourceContents blobResource = Assert.IsType<BlobResourceContents>(result.Contents[0]);
        Assert.NotNull(blobResource.Blob);
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
            Capabilities = new()
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

        await client.SubscribeToResourceAsync("test://static/resource/1", TestContext.Current.CancellationToken);

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
            Capabilities = new()
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
        await client.SubscribeToResourceAsync("test://static/resource/1", TestContext.Current.CancellationToken);

        // wait until we received a notification
        await receivedNotification.Task;

        // unsubscribe
        await client.UnsubscribeFromResourceAsync("test://static/resource/1", TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task Complete_Stdio_ResourceTemplateReference(string clientId)
    {
        // arrange

        // act
        await using var client = await _fixture.CreateClientAsync(clientId);
        var result = await client.CompleteAsync(
            new ResourceTemplateReference { Uri = "test://static/resource/1" },
            "argument_name", "1",
            TestContext.Current.CancellationToken
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
            new PromptReference { Name = "irrelevant" },
            argumentName: "style", argumentValue: "fo",
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(result);
        Assert.Single(result.Completion.Values);
        Assert.Equal("formal", result.Completion.Values[0]);
    }

    [Theory]
    [MemberData(nameof(GetClients))]
    public async Task Sampling_Stdio(string clientId)
    {
        // Set up the sampling handler
        int samplingHandlerCalls = 0;
        await using var client = await _fixture.CreateClientAsync(clientId, new()
        {
            Capabilities = new()
            {
                Sampling = new()
                {
                    SamplingHandler = async (_, _, _) =>
                    {
                        samplingHandlerCalls++;
                        return new CreateMessageResult
                        {
                            Model = "test-model",
                            Role = Role.Assistant,
                            Content = new TextContentBlock { Text = "Test response" },
                        };
                    },
                },
            },
        });

        // Call the server's sampleLLM tool which should trigger our sampling handler
        var result = await client.CallToolAsync(
            "sampleLLM",
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
            Capabilities = new()
            {
                Sampling = new()
                {
                    SamplingHandler = samplingHandler,
                },
            },
        }, cancellationToken: TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync("sampleLLM", new Dictionary<string, object?>()
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
            Capabilities = new()
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
        await client.SetLoggingLevel(LoggingLevel.Debug, TestContext.Current.CancellationToken);

        // assert
        await receivedNotification.Task;
    }

    [JsonSerializable(typeof(TestNotification))]
    partial class JsonContext3 : JsonSerializerContext;
}
