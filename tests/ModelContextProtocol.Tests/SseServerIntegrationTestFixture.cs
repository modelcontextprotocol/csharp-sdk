using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Configuration;
using ModelContextProtocol.Protocol.Transport;

namespace ModelContextProtocol.Tests;

public class SseServerIntegrationTestFixture : IAsyncDisposable
{
    private readonly CancellationTokenSource _stopCts = new();
    private readonly Task _serverTask;

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

        _serverTask = TestSseServer.Program.MainAsync([], _stopCts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        LoggerFactory.Dispose();
        _stopCts.Cancel();
        try
        {
            await _serverTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        _stopCts.Dispose();
    }
}