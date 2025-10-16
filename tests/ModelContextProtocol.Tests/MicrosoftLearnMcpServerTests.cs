using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests;

/// <summary>
/// Integration tests for connecting to the Microsoft Learn MCP server.
/// These tests connect to a live external service and are marked as Manual execution.
/// </summary>
public class MicrosoftLearnMcpServerTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    private const string MicrosoftLearnMcpEndpoint = "https://learn.microsoft.com/api/mcp";

    private Task<McpClient> CreateClientAsync(CancellationToken cancellationToken = default)
    {
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri(MicrosoftLearnMcpEndpoint),
            Name = "Microsoft Learn MCP Server",
            TransportMode = HttpTransportMode.StreamableHttp,
        };

        var clientOptions = new McpClientOptions
        {
            ClientInfo = new() { Name = "CSharpSdkIntegrationTest", Version = "1.0.0" }
        };

        return McpClient.CreateAsync(
            new HttpClientTransport(transportOptions),
            clientOptions,
            loggerFactory: LoggerFactory,
            cancellationToken: cancellationToken);
    }

    [Fact]
    [Trait("Execution", "Manual")]
    public async Task ConnectAndInitialize_MicrosoftLearnServer_WithStreamableHttp()
    {
        // Act
        await using var client = await CreateClientAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(client);
        Assert.NotNull(client.ServerCapabilities);
        Assert.NotNull(client.ServerInfo);
        Assert.NotNull(client.NegotiatedProtocolVersion);
    }

    [Fact]
    [Trait("Execution", "Manual")]
    public async Task ListTools_MicrosoftLearnServer()
    {
        // Act
        await using var client = await CreateClientAsync(TestContext.Current.CancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(tools);
        Assert.NotEmpty(tools);
    }

    [Fact]
    [Trait("Execution", "Manual")]
    public async Task ListResources_MicrosoftLearnServer()
    {
        // Act
        await using var client = await CreateClientAsync(TestContext.Current.CancellationToken);
        var resources = await client.ListResourcesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(resources);
        // Microsoft Learn server may or may not have resources, so we don't assert NotEmpty
    }

    [Fact]
    [Trait("Execution", "Manual")]
    public async Task ListPrompts_MicrosoftLearnServer()
    {
        // Act
        await using var client = await CreateClientAsync(TestContext.Current.CancellationToken);
        var prompts = await client.ListPromptsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(prompts);
        // Microsoft Learn server may or may not have prompts, so we don't assert NotEmpty
    }

    [Fact]
    [Trait("Execution", "Manual")]
    public async Task PingServer_MicrosoftLearnServer()
    {
        // Act
        await using var client = await CreateClientAsync(TestContext.Current.CancellationToken);
        await client.PingAsync(TestContext.Current.CancellationToken);

        // Assert - if we get here without exception, ping was successful
        Assert.True(true);
    }
}
