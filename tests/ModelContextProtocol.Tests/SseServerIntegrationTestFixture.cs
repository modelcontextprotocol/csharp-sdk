using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Test.Utils;
using ModelContextProtocol.Tests.Utils;
using ModelContextProtocol.TestSseServer;

namespace ModelContextProtocol.Tests;

public class SseServerIntegrationTestFixture : IAsyncDisposable
{
    private readonly KestrelInMemoryTransport _inMemoryTransport = new();

    private readonly Task _serverTask;
    private readonly CancellationTokenSource _stopCts = new();

    // XUnit's ITestOutputHelper is created per test, while this fixture is used for
    // multiple tests, so this dispatches the output to the current test.
    private readonly DelegatingTestOutputHelper _delegatingTestOutputHelper = new();

    private SseClientTransportOptions DefaultTransportOptions { get; } = new()
    {
        Endpoint = new Uri("http://localhost/sse"),
        Name = "TestServer",
    };

    public SseServerIntegrationTestFixture()
    {
        var socketsHttpHandler = new SocketsHttpHandler()
        {
            ConnectCallback = (context, token) =>
            {
                var connection = _inMemoryTransport.CreateConnection();
                return new(connection.ClientStream);
            },
        };

        HttpClient = new HttpClient(socketsHttpHandler)
        {
            BaseAddress = DefaultTransportOptions.Endpoint,
        };
        _serverTask = Program.MainAsync([], new XunitLoggerProvider(_delegatingTestOutputHelper), _inMemoryTransport, _stopCts.Token);
    }

    public HttpClient HttpClient { get; }

    public Task<IMcpClient> ConnectMcpClientAsync(McpClientOptions? options, ILoggerFactory loggerFactory)
    {
        return McpClientFactory.CreateAsync(
            new SseClientTransport(DefaultTransportOptions, HttpClient, loggerFactory),
            options,
            loggerFactory,
            TestContext.Current.CancellationToken);
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

        HttpClient.Dispose();
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
