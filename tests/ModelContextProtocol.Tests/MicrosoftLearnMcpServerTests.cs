using ModelContextProtocol.Client;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests;

public class MicrosoftLearnMcpServerTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    private const string MicrosoftLearnMcpEndpoint = "https://learn.microsoft.com/api/mcp";

    [Fact]
    [Trait("Execution", "Manual")]
    public async Task CanConnectToMicrosoftLearnMcpServer()
    {
        // Arrange
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri(MicrosoftLearnMcpEndpoint),
            Name = "Microsoft Learn MCP",
            TransportMode = HttpTransportMode.StreamableHttp,
        };

        var clientOptions = new McpClientOptions
        {
            ClientInfo = new() { Name = "MCP C# SDK Test Client", Version = "1.0.0" }
        };

        // Act
        await using var transport = new HttpClientTransport(transportOptions, loggerFactory: LoggerFactory);
        await using var client = await McpClient.CreateAsync(
            transport,
            clientOptions,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert - verify we successfully connected and initialized
        Assert.NotNull(client);
        Assert.NotNull(client.ServerInfo);
        Assert.NotEmpty(client.ServerInfo.Name);
    }

    [Fact]
    [Trait("Execution", "Manual")]
    public async Task CanListToolsFromMicrosoftLearnMcpServer()
    {
        // Arrange
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri(MicrosoftLearnMcpEndpoint),
            Name = "Microsoft Learn MCP",
            TransportMode = HttpTransportMode.StreamableHttp,
        };

        var clientOptions = new McpClientOptions
        {
            ClientInfo = new() { Name = "MCP C# SDK Test Client", Version = "1.0.0" }
        };

        // Act
        await using var transport = new HttpClientTransport(transportOptions, loggerFactory: LoggerFactory);
        await using var client = await McpClient.CreateAsync(
            transport,
            clientOptions,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert - verify we got tools from the server
        Assert.NotNull(tools);
        Assert.NotEmpty(tools);
    }

    [Fact]
    [Trait("Execution", "Manual")]
    public async Task CanListPromptsFromMicrosoftLearnMcpServer()
    {
        // Arrange
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri(MicrosoftLearnMcpEndpoint),
            Name = "Microsoft Learn MCP",
            TransportMode = HttpTransportMode.StreamableHttp,
        };

        var clientOptions = new McpClientOptions
        {
            ClientInfo = new() { Name = "MCP C# SDK Test Client", Version = "1.0.0" }
        };

        // Act
        await using var transport = new HttpClientTransport(transportOptions, loggerFactory: LoggerFactory);
        await using var client = await McpClient.CreateAsync(
            transport,
            clientOptions,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        var prompts = await client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert - verify we got prompts from the server (or an empty list if none are available)
        Assert.NotNull(prompts);
    }

    [Fact]
    [Trait("Execution", "Manual")]
    public async Task CanListResourcesFromMicrosoftLearnMcpServer()
    {
        // Arrange
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri(MicrosoftLearnMcpEndpoint),
            Name = "Microsoft Learn MCP",
            TransportMode = HttpTransportMode.StreamableHttp,
        };

        var clientOptions = new McpClientOptions
        {
            ClientInfo = new() { Name = "MCP C# SDK Test Client", Version = "1.0.0" }
        };

        // Act
        await using var transport = new HttpClientTransport(transportOptions, loggerFactory: LoggerFactory);
        await using var client = await McpClient.CreateAsync(
            transport,
            clientOptions,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert - verify we got resources from the server (or an empty list if none are available)
        Assert.NotNull(resources);
    }
}
