using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests;

public class SseIntegrationTests(ITestOutputHelper outputHelper) : LoggedTest(outputHelper)
{
    /// <summary>Port number to be grabbed by the next test.</summary>
    private static int s_nextPort = 3000;

    // If the tests run concurrently against different versions of the runtime, tests can conflict with
    // each other in the ports set up for interacting with containers. Ensure that such suites running
    // against different TFMs use different port numbers.
    private static readonly int s_portOffset = 1000 * (Environment.Version.Major switch
    {
        int v when v >= 8 => Environment.Version.Major - 7,
        _ => 0,
    });

    private static int CreatePortNumber() => Interlocked.Increment(ref s_nextPort) + s_portOffset;

    [Fact]
    public async Task ConnectAndReceiveMessage_InMemoryServer()
    {
        // Arrange
        await using InMemoryTestSseServer server = new(CreatePortNumber(), LoggerFactory.CreateLogger<InMemoryTestSseServer>());
        await server.StartAsync();

        var defaultOptions = new McpClientOptions
        {
            ClientInfo = new() { Name = "IntegrationTestClient", Version = "1.0.0" }
        };

        var defaultConfig = new McpServerConfig
        {
            Id = "test_server",
            Name = "In-memory Test Server",
            TransportType = TransportTypes.Sse,
            TransportOptions = [],
            Location = $"http://localhost:{server.Port}/sse"
        };

        // Act
        await using var client = await McpClientFactory.CreateAsync(
            defaultConfig, 
            defaultOptions,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        // Wait for SSE connection to be established
        await server.WaitForConnectionAsync(TimeSpan.FromSeconds(10));

        // Send a test message through POST endpoint
        await client.SendNotificationAsync("test/message", new { message = "Hello, SSE!" }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(true);
    }

    [Fact]
    [Trait("Execution", "Manual")]
    public async Task ConnectAndReceiveMessage_EverythingServerWithSse()
    {
        Assert.SkipWhen(!EverythingSseServerFixture.IsDockerAvailable, "docker is not available");

        int port = CreatePortNumber();

        await using var fixture = new EverythingSseServerFixture(port);
        await fixture.StartAsync();

        var defaultOptions = new McpClientOptions
        {
            ClientInfo = new() { Name = "IntegrationTestClient", Version = "1.0.0" }
        };

        var defaultConfig = new McpServerConfig
        {
            Id = "everything",
            Name = "Everything",
            TransportType = TransportTypes.Sse,
            TransportOptions = [],
            Location = $"http://localhost:{port}/sse"
        };

        // Create client and run tests
        await using var client = await McpClientFactory.CreateAsync(
            defaultConfig, 
            defaultOptions, 
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.NotEmpty(tools);
    }

    [Fact]
    [Trait("Execution", "Manual")]
    public async Task Sampling_Sse_EverythingServer()
    {
        Assert.SkipWhen(!EverythingSseServerFixture.IsDockerAvailable, "docker is not available");

        int port = CreatePortNumber();

        await using var fixture = new EverythingSseServerFixture(port);
        await fixture.StartAsync();

        var defaultConfig = new McpServerConfig
        {
            Id = "everything",
            Name = "Everything",
            TransportType = TransportTypes.Sse,
            TransportOptions = [],
            Location = $"http://localhost:{port}/sse"
        };

        int samplingHandlerCalls = 0;
        var defaultOptions = new McpClientOptions
        {
            ClientInfo = new()
            {
                Name = "IntegrationTestClient",
                Version = "1.0.0"
            },
            Capabilities = new()
            {
                Sampling = new()
                {
                    SamplingHandler = (_, _, _) =>
                    {
                        samplingHandlerCalls++;
                        return Task.FromResult(new CreateMessageResult
                        {
                            Model = "test-model",
                            Role = "assistant",
                            Content = new Content
                            {
                                Type = "text",
                                Text = "Test response"
                            }
                        });
                    },
                },
            },
        };

        await using var client = await McpClientFactory.CreateAsync(
            defaultConfig, 
            defaultOptions,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        // Call the server's sampleLLM tool which should trigger our sampling handler
        var result = await client.CallToolAsync("sampleLLM", new Dictionary<string, object?>
            {
                ["prompt"] = "Test prompt",
                ["maxTokens"] = 100
            }, cancellationToken: TestContext.Current.CancellationToken);

        // assert
        Assert.NotNull(result);
        var textContent = Assert.Single(result.Content);
        Assert.Equal("text", textContent.Type);
        Assert.False(string.IsNullOrEmpty(textContent.Text));
    }

    [Fact]
    public async Task ConnectAndReceiveMessage_InMemoryServer_WithFullEndpointEventUri()
    {
        // Arrange
        await using InMemoryTestSseServer server = new(CreatePortNumber(), LoggerFactory.CreateLogger<InMemoryTestSseServer>());
        server.UseFullUrlForEndpointEvent = true;
        await server.StartAsync();

        var defaultOptions = new McpClientOptions
        {
            ClientInfo = new() { Name = "IntegrationTestClient", Version = "1.0.0" }
        };

        var defaultConfig = new McpServerConfig
        {
            Id = "test_server",
            Name = "In-memory Test Server",
            TransportType = TransportTypes.Sse,
            TransportOptions = [],
            Location = $"http://localhost:{server.Port}/sse"
        };

        // Act
        await using var client = await McpClientFactory.CreateAsync(
            defaultConfig,
            defaultOptions,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        // Wait for SSE connection to be established
        await server.WaitForConnectionAsync(TimeSpan.FromSeconds(10));

        // Send a test message through POST endpoint
        await client.SendNotificationAsync("test/message", new { message = "Hello, SSE!" }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(true);
    }

    [Fact]
    public async Task ConnectAndReceiveNotification_InMemoryServer()
    {
        // Arrange
        await using InMemoryTestSseServer server = new(CreatePortNumber(), LoggerFactory.CreateLogger<InMemoryTestSseServer>());
        await server.StartAsync();


        var defaultOptions = new McpClientOptions
        {
            ClientInfo = new() { Name = "IntegrationTestClient", Version = "1.0.0" }
        };

        var defaultConfig = new McpServerConfig
        {
            Id = "test_server",
            Name = "In-memory Test Server",
            TransportType = TransportTypes.Sse,
            TransportOptions = [],
            Location = $"http://localhost:{server.Port}/sse"
        };

        // Act
        await using var client = await McpClientFactory.CreateAsync(
            defaultConfig, 
            defaultOptions, 
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        // Wait for SSE connection to be established
        await server.WaitForConnectionAsync(TimeSpan.FromSeconds(10));

        var receivedNotification = new TaskCompletionSource<string?>();
        client.AddNotificationHandler("test/notification", (args) =>
            {
                var msg = args.Params?["message"]?.GetValue<string>();
                receivedNotification.SetResult(msg);

                return Task.CompletedTask;
            });

        // Act
        await server.SendTestNotificationAsync("Hello from server!");

        // Assert
        var message = await receivedNotification.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal("Hello from server!", message);
    }
}
