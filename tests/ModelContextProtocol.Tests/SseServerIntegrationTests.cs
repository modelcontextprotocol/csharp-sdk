using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Types;

namespace ModelContextProtocol.Tests;

public class SseServerIntegrationTests : IClassFixture<SseServerIntegrationTestFixture>
{
    private readonly SseServerIntegrationTestFixture _fixture;

    public SseServerIntegrationTests(SseServerIntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private Task<IMcpClient> GetClientAsync(McpClientOptions? options = null)
    {
        return McpClientFactory.CreateAsync(
            _fixture.DefaultConfig,
            options ?? _fixture.DefaultOptions,
            loggerFactory: _fixture.LoggerFactory);
    }

    [Fact]
    public async Task ConnectAndPing_Sse_TestServer()
    {
        // Arrange

        // Act
        var client = await GetClientAsync();
        await client.PingAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(client);
    }

    [Fact]
    public async Task Connect_TestServer_ShouldProvideServerFields()
    {
        // Arrange

        // Act
        var client = await GetClientAsync();

        // Assert
        Assert.NotNull(client.ServerCapabilities);
        Assert.NotNull(client.ServerInfo);
    }

    [Fact]
    public async Task ListTools_Sse_TestServer()
    {        
        // arrange

        // act
        var client = await GetClientAsync();
        var tools = await client.ListToolsAsync(TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);

        // assert
        Assert.NotNull(tools);
    }

    [Fact]
    public async Task CallTool_Sse_EchoServer()
    {
        // arrange

        // act
        var client = await GetClientAsync();
        var result = await client.CallToolAsync(
            "echo",
            new Dictionary<string, object>
            {
                ["message"] = "Hello MCP!"
            },
            CancellationToken.None
        );

        // assert
        Assert.NotNull(result);
        Assert.False(result.IsError);
        var textContent = Assert.Single(result.Content, c => c.Type == "text");
        Assert.Equal("Echo: Hello MCP!", textContent.Text);
    }

    [Fact]
    public async Task ListResources_Sse_TestServer()
    {
        // arrange

        // act
        var client = await GetClientAsync();

        List<Resource> allResources = [];
        string? cursor = null;
        do
        {
            var resources = await client.ListResourcesAsync(cursor, CancellationToken.None);
            allResources.AddRange(resources.Resources);
            cursor = resources.NextCursor;
        }
        while (cursor != null);

        // The everything server provides 100 test resources
        Assert.Equal(100, allResources.Count);
    }

    [Fact]
    public async Task ReadResource_Sse_TextResource()
    {
        // arrange

        // act
        var client = await GetClientAsync();
        // Odd numbered resources are text in the everything server (despite the docs saying otherwise)
        // 1 is index 0, which is "even" in the 0-based index
        // We copied this oddity to the test server
        var result = await client.ReadResourceAsync("test://static/resource/1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result.Contents);
        Assert.NotNull(result.Contents[0].Text);
    }

    [Fact]
    public async Task ReadResource_Sse_BinaryResource()
    {
        // arrange

        // act
        var client = await GetClientAsync();
        // Even numbered resources are binary in the everything server (despite the docs saying otherwise)
        // 2 is index 1, which is "odd" in the 0-based index
        // We copied this oddity to the test server
        var result = await client.ReadResourceAsync("test://static/resource/2", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result.Contents);
        Assert.NotNull(result.Contents[0].Blob);
    }

    [Fact]
    public async Task ListPrompts_Sse_TestServer()
    {
        // arrange

        // act
        var client = await GetClientAsync();
        var prompts = await client.ListPromptsAsync(TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);

        // assert
        Assert.NotNull(prompts);
        Assert.NotEmpty(prompts);
        // We could add specific assertions for the known prompts
        Assert.Contains(prompts, p => p.Name == "simple_prompt");
        Assert.Contains(prompts, p => p.Name == "complex_prompt");
    }

    [Fact]
    public async Task GetPrompt_Sse_SimplePrompt()
    {
        // arrange

        // act
        var client = await GetClientAsync();
        var result = await client.GetPromptAsync("simple_prompt", null, CancellationToken.None);

        // assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Messages);
    }

    [Fact]
    public async Task GetPrompt_Sse_ComplexPrompt()
    {
        // arrange

        // act
        var client = await GetClientAsync();
        var arguments = new Dictionary<string, object>
        {
            { "temperature", "0.7" },
            { "style", "formal" }
        };
        var result = await client.GetPromptAsync("complex_prompt", arguments, CancellationToken.None);

        // assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Messages);
    }

    [Fact]
    public async Task GetPrompt_Sse_NonExistent_ThrowsException()
    {
        // arrange

        // act
        var client = await GetClientAsync();
        await Assert.ThrowsAsync<McpClientException>(() =>
            client.GetPromptAsync("non_existent_prompt", null, CancellationToken.None));
    }

    [Fact]
    public async Task Sampling_Sse_TestServer()
    {
        // arrange
        // Set up the sampling handler
        int samplingHandlerCalls = 0;
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        var options =  _fixture.DefaultOptions with
        {
            Capabilities = new()
            {
                Sampling = new()
                {
                    SamplingHandler = async (_, _) =>
                    {
                        samplingHandlerCalls++;
                        return new CreateMessageResult
                        {
                            Model = "test-model",
                            Role = "assistant",
                            Content = new Content
                            {
                                Type = "text",
                                Text = "Test response"
                            }
                        };
                    }
                }
            }
        };
        var client = await GetClientAsync(options);
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously

        // Call the server's sampleLLM tool which should trigger our sampling handler
        var result = await client.CallToolAsync("sampleLLM", new Dictionary<string, object>
            {
                ["prompt"] = "Test prompt",
                ["maxTokens"] = 100
            },
            TestContext.Current.CancellationToken);

        // assert
        Assert.NotNull(result);
        var textContent = Assert.Single(result.Content);
        Assert.Equal("text", textContent.Type);
        Assert.False(string.IsNullOrEmpty(textContent.Text));
    }
}
