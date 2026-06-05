using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using System.Runtime.InteropServices;

namespace ModelContextProtocol.Tests;

public class ClientIntegrationTestFixture
{
    private ILoggerFactory? _loggerFactory;

    public StdioClientTransportOptions EverythingServerTransportOptions { get; }
    public StdioClientTransportOptions TestServerTransportOptions { get; }

    public static IEnumerable<string> ClientIds => ["everything", "test_server"];

    public ClientIntegrationTestFixture()
    {
        const string ServerEverythingVersion = "2026.1.26";

        EverythingServerTransportOptions = new()
        {
            Command = "npx",
            // Change to Arguments = ["mcp-server-everything"] if you want to run the server locally after creating a symlink
            Arguments = ["-y", "--verbose", $"@modelcontextprotocol/server-everything@{ServerEverythingVersion}"],
            Name = "Everything",
        };

        TestServerTransportOptions = new()
        {
            Command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "TestServer.exe" : PlatformDetection.IsMonoRuntime ? "mono" : "dotnet",
            Name = "TestServer",
        };

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Change to Arguments to "mcp-server-everything" if you want to run the server locally after creating a symlink
            TestServerTransportOptions.Arguments = [PlatformDetection.IsMonoRuntime ? "TestServer.exe" : "TestServer.dll"];
        }
    }

    public void Initialize(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public Task<McpClient> CreateClientAsync(string clientId, McpClientOptions? clientOptions = null) =>
        McpClient.CreateAsync(new StdioClientTransport(clientId switch
        {
            "everything" => EverythingServerTransportOptions,
            "test_server" => TestServerTransportOptions,
            _ => throw new ArgumentException($"Unknown client ID: {clientId}")
        }), clientOptions, loggerFactory: _loggerFactory);
}