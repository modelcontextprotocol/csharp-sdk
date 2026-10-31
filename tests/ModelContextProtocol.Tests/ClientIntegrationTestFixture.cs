using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Tests.Utils;
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
        string testServerExecutable = Path.Combine(AppContext.BaseDirectory, "TestServer.exe");
        string testServerDll = Path.Combine(AppContext.BaseDirectory, "TestServer.dll");

        EverythingServerTransportOptions = new()
        {
            Command = "node",
            // To use a local Everything server, build it, then run `npm link <path-to-server-everything>`
            // from this repository root after `npm ci`. The linked package must contain dist/index.js;
            // no argument changes are needed because this path resolves through the package link.
            Arguments = [NodeHelpers.GetTestServerScriptPath("everything")],
            Name = "Everything",
        };

        TestServerTransportOptions = new()
        {
            Command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? testServerExecutable : PlatformDetection.IsMonoRuntime ? "mono" : "dotnet",
            Name = "TestServer",
        };

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            TestServerTransportOptions.Arguments = [PlatformDetection.IsMonoRuntime ? testServerExecutable : testServerDll];
        }
    }

    public void Initialize(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public Task<McpClient> CreateClientAsync(string clientId, McpClientOptions? clientOptions = null)
    {
        if (clientId == "test_server" && clientOptions is null)
        {
            clientOptions = TestMcpClient.CreateOptions();
        }

        return McpClient.CreateAsync(new StdioClientTransport(clientId switch
        {
            "everything" => EverythingServerTransportOptions,
            "test_server" => TestServerTransportOptions,
            _ => throw new ArgumentException($"Unknown client ID: {clientId}")
        }), clientOptions, loggerFactory: _loggerFactory);
    }
}