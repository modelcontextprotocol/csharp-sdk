using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Test.Utils;
using ModelContextProtocol.Tests.Utils;
using ModelContextProtocol.TestSseServer;

namespace ModelContextProtocol.Tests;

public class SseServerIntegrationTestFixture : IAsyncDisposable
{
    private readonly Task _serverTask;
    private readonly CancellationTokenSource _stopCts = new();

    private readonly DelegatingTestOutputHelper _delegatingTestOutputHelper = new();

    public McpServerConfig DefaultConfig { get; }

    public SseServerIntegrationTestFixture()
    {
        DefaultConfig = new McpServerConfig
        {
            Id = "test_server",
            Name = "TestServer",
            TransportType = TransportTypes.Sse,
            TransportOptions = [],
            Location = "http://localhost:3001/sse"
        };

        _serverTask = Program.MainAsync([], new XunitLoggerProvider(_delegatingTestOutputHelper), _stopCts.Token);
    }

    public static McpClientOptions CreateDefaultClientOptions() => new()
    {
        ClientInfo = new() { Name = "IntegrationTestClient", Version = "1.0.0" },
    };

    public void Initialize(ITestOutputHelper output)
    {
        _delegatingTestOutputHelper.CurrentTestOutputHelper = output;
    }

    public void TestCompleted()
    {
        _delegatingTestOutputHelper.CurrentTestOutputHelper = null;
    }

    public async ValueTask DisposeAsync()
    {
        _delegatingTestOutputHelper.CurrentTestOutputHelper = null;
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
