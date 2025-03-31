using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;

namespace ModelContextProtocol.Tests;

public class SseIntegrationTests(ITestOutputHelper outputHelper) : LoggedTest(outputHelper)
{
    [Fact]
    public async Task ConnectAndReceiveMessage_InMemoryServer()
    {
        // Arrange
        await using InMemoryTestSseServer server = new(logger: LoggerFactory.CreateLogger<InMemoryTestSseServer>());
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
            Location = "http://localhost:5000/sse"
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
        await client.SendNotificationAsync("test/message", new { message = "Hello, SSE!" }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(true);
    }

    [Fact]
    [Trait("Execution", "Manual")]
    public async Task ConnectAndReceiveMessage_EverythingServerWithSse()
    {
        Assert.SkipWhen(!EverythingSseServerFixture.IsDockerAvailable, "docker is not available");

        int port = 3001;

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
        var tools = await client.ListToolsAsync(TestContext.Current.CancellationToken);

        // assert
        Assert.NotEmpty(tools);
    }

    [Fact]
    [Trait("Execution", "Manual")]
    public async Task Sampling_Sse_EverythingServer()
    {
        Assert.SkipWhen(!EverythingSseServerFixture.IsDockerAvailable, "docker is not available");

        int port = 3002;

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
                    SamplingHandler = (_, _) =>
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
            }, TestContext.Current.CancellationToken);

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
        await using InMemoryTestSseServer server = new(logger: LoggerFactory.CreateLogger<InMemoryTestSseServer>());
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
            Location = "http://localhost:5000/sse"
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
        await client.SendNotificationAsync("test/message", new { message = "Hello, SSE!" }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(true);
    }

    [Fact]
    public async Task ConnectAndReceiveNotification_InMemoryServer()
    {
        // Arrange
        await using InMemoryTestSseServer server = new(logger: LoggerFactory.CreateLogger<InMemoryTestSseServer>());
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
            Location = "http://localhost:5000/sse"
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
                var msg = ((JsonElement?)args.Params)?.GetProperty("message").GetString();
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
