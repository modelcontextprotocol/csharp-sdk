using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using Microsoft.Extensions.Logging;

namespace ModelContextProtocol.Tests;

public class ClientIntegrationTestFixture
{
    private ILoggerFactory? _loggerFactory;

    public StdioClientTransportOptions EverythingServerConfig { get; }
    public StdioClientTransportOptions TestServerConfig { get; }

    public static IEnumerable<string> ClientIds => ["everything", "test_server"];

    public ClientIntegrationTestFixture()
    {
        EverythingServerConfig = new()
        {
            Id = "everything",
            Name = "Everything",
            Command = "npx",
            // Change to Arguments = "mcp-server-everything" if you want to run the server locally after creating a symlink
            Arguments = "-y --verbose @modelcontextprotocol/server-everything"
        };

        TestServerConfig = new()
        {
            Id = "test_server",
            Name = "TestServer",
            Command = OperatingSystem.IsWindows() ? "TestServer.exe" : "dotnet",
        };

        if (!OperatingSystem.IsWindows())
        {
            // Change to Arguments to "mcp-server-everything" if you want to run the server locally after creating a symlink
            TestServerConfig.Arguments = "TestServer.dll";
        }
    }

    public void Initialize(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public Task<IMcpClient> CreateClientAsync(string clientId, McpClientOptions? clientOptions = null) =>
        McpClientFactory.CreateAsync(new StdioClientTransport(clientId switch
        {
            "everything" => EverythingServerConfig,
            "test_server" => TestServerConfig,
            _ => throw new ArgumentException($"Unknown client ID: {clientId}")
        }), clientOptions, loggerFactory: _loggerFactory);
}