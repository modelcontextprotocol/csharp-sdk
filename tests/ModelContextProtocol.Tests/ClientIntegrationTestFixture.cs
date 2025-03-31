using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using Microsoft.Extensions.Logging;

namespace ModelContextProtocol.Tests;

public class ClientIntegrationTestFixture
{
    private ILoggerFactory? _loggerFactory;

    public McpClientOptions DefaultOptions { get; }
    public McpServerConfig EverythingServerConfig { get; }
    public McpServerConfig TestServerConfig { get; }

    public static IEnumerable<string> ClientIds => ["everything", "test_server"];

    public ClientIntegrationTestFixture()
    {
        DefaultOptions = new()
        {
            ClientInfo = new() { Name = "IntegrationTestClient", Version = "1.0.0" },
        };

        EverythingServerConfig = new()
        {
            Id = "everything",
            Name = "Everything",
            TransportType = TransportTypes.StdIo,
            TransportOptions = new Dictionary<string, string>
            {
                ["command"] = "npx",
                // Change to ["arguments"] = "mcp-server-everything" if you want to run the server locally after creating a symlink
                ["arguments"] = "-y --verbose @modelcontextprotocol/server-everything"
            }
        };

        TestServerConfig = new()
        {
            Id = "test_server",
            Name = "TestServer",
            TransportType = TransportTypes.StdIo,
            TransportOptions = new Dictionary<string, string>
            {
                ["command"] = OperatingSystem.IsWindows() ? "TestServer.exe" : "dotnet",
                // Change to ["arguments"] = "mcp-server-everything" if you want to run the server locally after creating a symlink
            }
        };

        if (!OperatingSystem.IsWindows())
        {
            TestServerConfig.TransportOptions["arguments"] = "TestServer.dll";
        }
    }

    public void Initialize(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public Task<IMcpClient> CreateClientAsync(string clientId, McpClientOptions? clientOptions = null) =>
        McpClientFactory.CreateAsync(clientId switch
        {
            "everything" => EverythingServerConfig,
            "test_server" => TestServerConfig,
            _ => throw new ArgumentException($"Unknown client ID: {clientId}")
        }, clientOptions ?? DefaultOptions, loggerFactory: _loggerFactory);
}