﻿using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using Microsoft.Extensions.Logging;

namespace ModelContextProtocol.Tests;

public class ClientIntegrationTestFixture
{
    private ILoggerFactory? _loggerFactory;

    public StdioClientTransportOptions EverythingServerTransportOptions { get; }
    public StdioClientTransportOptions TestServerTransportOptions { get; }

    public static IEnumerable<string> ClientIds => ["everything", "test_server"];

    public ClientIntegrationTestFixture()
    {
        EverythingServerTransportOptions = new()
        {
            Command = "npx",
            // Change to Arguments = ["mcp-server-everything"] if you want to run the server locally after creating a symlink
            Arguments = ["-y", "--verbose", "@modelcontextprotocol/server-everything"],
            Name = "Everything",
        };

        TestServerTransportOptions = new()
        {
            Command = OperatingSystem.IsWindows() ? "TestServer.exe" : "dotnet",
            Name = "TestServer",
        };

        if (!OperatingSystem.IsWindows())
        {
            // Change to Arguments to "mcp-server-everything" if you want to run the server locally after creating a symlink
            TestServerTransportOptions.Arguments = ["TestServer.dll"];
        }
    }

    public void Initialize(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public Task<IMcpClient> CreateClientAsync(string clientId, McpClientOptions? clientOptions = null) =>
        McpClientFactory.CreateAsync(new StdioClientTransport(clientId switch
        {
            "everything" => EverythingServerTransportOptions,
            "test_server" => TestServerTransportOptions,
            _ => throw new ArgumentException($"Unknown client ID: {clientId}")
        }), clientOptions, loggerFactory: _loggerFactory);
}