using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Configuration;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Test.Utils;
using ModelContextProtocol.TestSseServer;

namespace ModelContextProtocol.Tests;

public class SseServerIntegrationTestFixture : IAsyncDisposable
{
    private readonly Task _serverTask;
    private readonly CancellationTokenSource _stopCts = new();

    private readonly DelegatingTestOutputHelper _delegatingTestOutputHelper = new();
    private readonly ILoggerFactory _redirectingLoggerFactory;

    public McpClientOptions DefaultOptions { get; }
    public McpServerConfig DefaultConfig { get; }

    public SseServerIntegrationTestFixture()
    {
        _redirectingLoggerFactory = LoggerFactory.Create(builder =>
        {
            Program.ConfigureSerilog(builder);
            builder.AddProvider(new XunitLoggerProvider(_delegatingTestOutputHelper));
        });

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

        _serverTask = Program.MainAsync([], _redirectingLoggerFactory, _stopCts.Token);
    }

    public void Initialize(ITestOutputHelper output)
    {
        _delegatingTestOutputHelper.CurrentTestOutputHelper = output;
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
        _redirectingLoggerFactory.Dispose();
        _stopCts.Dispose();
    }

    private class DelegatingTestOutputHelper() : ITestOutputHelper
    {
        public ITestOutputHelper? CurrentTestOutputHelper { get; set; }

        public string Output => CurrentTestOutputHelper?.Output ?? string.Empty;

        public void Write(string message) => CurrentTestOutputHelper?.Write(message);
        public void Write(string format, params object[] args) => CurrentTestOutputHelper?.Write(format, args);
        public void WriteLine(string message) => CurrentTestOutputHelper?.WriteLine(message);
        public void WriteLine(string format, params object[] args) => CurrentTestOutputHelper?.WriteLine(format, args);
    }
}