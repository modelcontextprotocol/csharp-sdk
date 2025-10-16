using ModelContextProtocol.Client;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests;

/// <summary>
/// Tests for connecting to the Microsoft Learn MCP server.
/// These tests connect to a real external server and are marked as manual execution.
/// </summary>
public class MicrosoftLearnServerTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    /// <summary>
    /// Tests connecting to the Microsoft Learn MCP server via streamable HTTP.
    /// This test requires internet connectivity and access to learn.microsoft.com.
    /// </summary>
    [Fact]
    [Trait("Execution", "Manual")]
    public async Task CanConnectToMicrosoftLearnServerViaStreamableHttp()
    {
        // Arrange
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri("https://learn.microsoft.com/api/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            Name = "Microsoft Learn MCP Server",
        };

        // Act
        await using var transport = new HttpClientTransport(transportOptions, LoggerFactory);
        await using var client = await McpClient.CreateAsync(
            transport,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert - verify we can list tools from the server
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(tools);
        
        // The server should expose some tools - we don't assert specific tools as they may change
        TestOutputHelper.WriteLine($"Connected to Microsoft Learn MCP server successfully.");
        TestOutputHelper.WriteLine($"Server: {client.ServerInfo.Name} v{client.ServerInfo.Version}");
        TestOutputHelper.WriteLine($"Protocol version: {client.NegotiatedProtocolVersion}");
        TestOutputHelper.WriteLine($"Number of tools available: {tools.Count}");
        
        if (tools.Count > 0)
        {
            TestOutputHelper.WriteLine("\nAvailable tools:");
            foreach (var tool in tools)
            {
                TestOutputHelper.WriteLine($"  - {tool.Name}: {tool.Description}");
            }
        }
    }

    /// <summary>
    /// Tests connecting to the Microsoft Learn MCP server and verifying server capabilities.
    /// This test requires internet connectivity and access to learn.microsoft.com.
    /// </summary>
    [Fact]
    [Trait("Execution", "Manual")]
    public async Task MicrosoftLearnServer_HasExpectedCapabilities()
    {
        // Arrange
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri("https://learn.microsoft.com/api/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            Name = "Microsoft Learn MCP Server",
        };

        // Act
        await using var transport = new HttpClientTransport(transportOptions, LoggerFactory);
        await using var client = await McpClient.CreateAsync(
            transport,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert - verify basic server info
        Assert.NotNull(client.ServerInfo);
        Assert.False(string.IsNullOrEmpty(client.ServerInfo.Name), "Server should have a name");
        Assert.False(string.IsNullOrEmpty(client.NegotiatedProtocolVersion), "Should have negotiated a protocol version");

        // Log server capabilities
        TestOutputHelper.WriteLine($"Server: {client.ServerInfo.Name}");
        if (!string.IsNullOrEmpty(client.ServerInfo.Version))
        {
            TestOutputHelper.WriteLine($"Version: {client.ServerInfo.Version}");
        }
        TestOutputHelper.WriteLine($"Protocol: {client.NegotiatedProtocolVersion}");
        
        TestOutputHelper.WriteLine("\nCapabilities:");
        TestOutputHelper.WriteLine($"  - Tools: {client.ServerCapabilities.Tools is not null}");
        TestOutputHelper.WriteLine($"  - Prompts: {client.ServerCapabilities.Prompts is not null}");
        TestOutputHelper.WriteLine($"  - Resources: {client.ServerCapabilities.Resources is not null}");
    }
}
