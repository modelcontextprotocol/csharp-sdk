using ModelContextProtocol.Client;
using ModelContextProtocol.Configuration;
using ModelContextProtocol.Protocol.Transport;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Tests;

public class SseServerIntegrationTestFixture : IDisposable
{
    private Process _process;

    public ILoggerFactory LoggerFactory { get; }
    public McpClientOptions DefaultOptions { get; }
    public McpServerConfig DefaultConfig { get; }

    public SseServerIntegrationTestFixture()
    {
        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            builder.AddConsole()
            .SetMinimumLevel(LogLevel.Debug));

        DefaultOptions = new()
        {
            ClientInfo = new() { Name = "IntegrationTestClient", Version = "1.0.0" },
        };

        DefaultConfig = new McpServerConfig
        {
            Id = "test_server",
            Name = "TestServer",
            TransportType = TransportTypes.Sse,
            TransportOptions = [],
            Location = "http://localhost:3001/sse"
        };

        Start();
    }

    [MemberNotNull(nameof(_process))]
    public void Start()
    {
        // Start the server (which is at TestSseServer.exe on windows and "dotnet TestSseServer.dll" on linux)
        var processStartInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "TestSseServer.exe" : "dotnet",
            Arguments = "TestSseServer.dll",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        _process = Process.Start(processStartInfo)
            ?? throw new InvalidOperationException($"Could not start process for {processStartInfo.FileName} with '{processStartInfo.Arguments}'.");

        // Wait 1 second
        Thread.Sleep(1000);
    }

    public void Dispose()
    {
        try
        {
            LoggerFactory?.Dispose();
        }
        finally
        {
            // Kill the server process
            _process.Kill();
        }
    }
}