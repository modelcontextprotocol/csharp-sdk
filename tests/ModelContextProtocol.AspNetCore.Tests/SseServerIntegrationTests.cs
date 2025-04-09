using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Tests.Utils;
using System.Net;
using System.Text;

namespace ModelContextProtocol.Tests;

public class SseServerIntegrationTests : LoggedTest, IClassFixture<SseServerIntegrationTestFixture>
{
    private readonly SseServerIntegrationTestFixture _fixture;

    public SseServerIntegrationTests(SseServerIntegrationTestFixture fixture, ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        _fixture = fixture;
        _fixture.Initialize(testOutputHelper);
    }

    public override void Dispose()
    {
        _fixture.TestCompleted();
        base.Dispose();
    }

    private Task<IMcpClient> GetClientAsync(McpClientOptions? options = null)
    {
        return _fixture.ConnectMcpClientAsync(options, LoggerFactory);
    }

    [Fact]
    public async Task ConnectAndPing_Sse_TestServer()
    {
        // Arrange

        // Act
        await using var client = await GetClientAsync();
        await client.PingAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(client);
    }

    [Fact]
    public async Task Connect_TestServer_ShouldProvideServerFields()
    {
        // Arrange

        // Act
        await using var client = await GetClientAsync();

        // Assert
        Assert.NotNull(client.ServerCapabilities);
        Assert.NotNull(client.ServerInfo);
    }

    [Fact]
    public async Task ListTools_Sse_TestServer()
    {        
        // arrange

        // act
        await using var client = await GetClientAsync();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.NotNull(tools);
    }

    [Fact]
    public async Task CallTool_Sse_EchoServer()
    {
        // arrange

        // act
        await using var client = await GetClientAsync();
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
        Assert.False(result.IsError);
        var textContent = Assert.Single(result.Content, c => c.Type == "text");
        Assert.Equal("Echo: Hello MCP!", textContent.Text);
    }

    [Fact]
    public async Task ListResources_Sse_TestServer()
    {
        // arrange

        // act
        await using var client = await GetClientAsync();

        IList<Resource> allResources = await client.ListResourcesAsync(TestContext.Current.CancellationToken);

        // The everything server provides 100 test resources
        Assert.Equal(100, allResources.Count);
    }

    [Fact]
    public async Task ReadResource_Sse_TextResource()
    {
        // arrange

        // act
        await using var client = await GetClientAsync();
        // Odd numbered resources are text in the everything server (despite the docs saying otherwise)
        // 1 is index 0, which is "even" in the 0-based index
        // We copied this oddity to the test server
        var result = await client.ReadResourceAsync("test://static/resource/1", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.Contents);

        TextResourceContents textContent = Assert.IsType<TextResourceContents>(result.Contents[0]);
        Assert.NotNull(textContent.Text);
    }

    [Fact]
    public async Task ReadResource_Sse_BinaryResource()
    {
        // arrange

        // act
        await using var client = await GetClientAsync();
        // Even numbered resources are binary in the everything server (despite the docs saying otherwise)
        // 2 is index 1, which is "odd" in the 0-based index
        // We copied this oddity to the test server
        var result = await client.ReadResourceAsync("test://static/resource/2", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.Contents);

        BlobResourceContents blobContent = Assert.IsType<BlobResourceContents>(result.Contents[0]);
        Assert.NotNull(blobContent.Blob);
    }

    [Fact]
    public async Task ListPrompts_Sse_TestServer()
    {
        // arrange

        // act
        await using var client = await GetClientAsync();
        var prompts = await client.ListPromptsAsync(TestContext.Current.CancellationToken);

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
        await using var client = await GetClientAsync();
        var result = await client.GetPromptAsync("simple_prompt", null, cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Messages);
    }

    [Fact]
    public async Task GetPrompt_Sse_ComplexPrompt()
    {
        // arrange

        // act
        await using var client = await GetClientAsync();
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

    [Fact]
    public async Task GetPrompt_Sse_NonExistent_ThrowsException()
    {
        // arrange

        // act
        await using var client = await GetClientAsync();
        await Assert.ThrowsAsync<McpException>(() =>
            client.GetPromptAsync("non_existent_prompt", null, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Sampling_Sse_TestServer()
    {
        // arrange
        // Set up the sampling handler
        int samplingHandlerCalls = 0;
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        McpClientOptions options = new();
        options.Capabilities = new();
        options.Capabilities.Sampling ??= new();
        options.Capabilities.Sampling.SamplingHandler = async (_, _, _) =>
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
        };
        await using var client = await GetClientAsync(options);
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously

        // Call the server's sampleLLM tool which should trigger our sampling handler
        var result = await client.CallToolAsync("sampleLLM", new Dictionary<string, object?>
            {
                ["prompt"] = "Test prompt",
                ["maxTokens"] = 100
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.NotNull(result);
        var textContent = Assert.Single(result.Content);
        Assert.Equal("text", textContent.Type);
        Assert.False(string.IsNullOrEmpty(textContent.Text));
    }

    [Fact]
    public async Task CallTool_Sse_EchoServer_Concurrently()
    {
        await using var client1 = await GetClientAsync();
        await using var client2 = await GetClientAsync();

        for (int i = 0; i < 4; i++)
        {
            var client = (i % 2 == 0) ? client1 : client2;
            var result =  await client.CallToolAsync(
                "echo",
                new Dictionary<string, object?>
                {
                    ["message"] = $"Hello MCP! {i}"
                },
                cancellationToken: TestContext.Current.CancellationToken
            );

            Assert.NotNull(result);
            Assert.False(result.IsError);
            var textContent = Assert.Single(result.Content, c => c.Type == "text");
            Assert.Equal($"Echo: Hello MCP! {i}", textContent.Text);
        }
    }

    [Fact]
    public async Task EventSourceResponse_Includes_ExpectedHeaders()
    {
        using var sseResponse = await _fixture.HttpClient.GetAsync("", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        sseResponse.EnsureSuccessStatusCode();

        Assert.Equal("text/event-stream", sseResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("identity", sseResponse.Content.Headers.ContentEncoding.ToString());
        Assert.NotNull(sseResponse.Headers.CacheControl);
        Assert.True(sseResponse.Headers.CacheControl.NoStore);
        Assert.True(sseResponse.Headers.CacheControl.NoCache);
    }

    [Fact]
    public async Task EventSourceStream_Includes_MessageEventType()
    {
        // Simulate our own MCP client handshake using a plain HttpClient so we can look for "event: message"
        // in the raw SSE response stream which is not exposed by the real MCP client.
        await using var sseResponse = await _fixture.HttpClient.GetStreamAsync("", TestContext.Current.CancellationToken);
        using var streamReader = new StreamReader(sseResponse);

        var endpointEvent = await streamReader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.Equal("event: endpoint", endpointEvent);

        var endpointData = await streamReader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(endpointData);
        Assert.StartsWith("data: ", endpointData);
        var messageEndpoint = endpointData["data: ".Length..];

        const string initializeRequest = """
            {"jsonrpc":"2.0","id":"1","method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"IntegrationTestClient","version":"1.0.0"}}}
            """;
        using (var initializeRequestBody = new StringContent(initializeRequest, Encoding.UTF8, "application/json"))
        {
            var response = await _fixture.HttpClient.PostAsync(messageEndpoint, initializeRequestBody, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        const string initializedNotification = """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """;
        using (var initializedNotificationBody = new StringContent(initializedNotification, Encoding.UTF8, "application/json"))
        {
            var response = await _fixture.HttpClient.PostAsync(messageEndpoint, initializedNotificationBody, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        Assert.Equal("", await streamReader.ReadLineAsync(TestContext.Current.CancellationToken));
        var messageEvent = await streamReader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.Equal("event: message", messageEvent);
    }
}
