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
        // Ensure that test suites running against different TFMs and possibly concurrently use different port numbers.
        int port = 3001 + Environment.Version.Major;

        DefaultConfig = new McpServerConfig
        {
            Id = "test_server",
            Name = "TestServer",
            TransportType = TransportTypes.Sse,
            TransportOptions = [],
            Location = $"http://localhost:{port}/sse"
        };

        _serverTask = Program.MainAsync([port.ToString()], new XunitLoggerProvider(_delegatingTestOutputHelper), _stopCts.Token);
    }

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
