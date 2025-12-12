using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Tests.Utils;
using ModelContextProtocol.TestSseServer;
using System.Net;

namespace ModelContextProtocol.AspNetCore.Tests;

public class SseServerIntegrationTestFixture : IAsyncDisposable
{
    private readonly KestrelInMemoryTransport _inMemoryTransport = new();

    private readonly Task _serverTask;
    private readonly CancellationTokenSource _stopCts = new();

    // XUnit's ITestOutputHelper is created per test, while this fixture is used for
    // multiple tests, so this dispatches the output to the current test.
    private readonly DelegatingTestOutputHelper _delegatingTestOutputHelper = new();

    private HttpClientTransportOptions DefaultTransportOptions { get; set; } = new()
    {
        Endpoint = new("http://localhost:5000/"),
    };

    public SseServerIntegrationTestFixture()
    {
        var socketsHttpHandler = new SocketsHttpHandler
        {
            ConnectCallback = (context, token) =>
            {
                var connection = _inMemoryTransport.CreateConnection(new DnsEndPoint("localhost", 5000));
                return new(connection.ClientStream);
            },
        };

        HttpClient = new HttpClient(socketsHttpHandler)
        {
            BaseAddress = new("http://localhost:5000/"),
        };

        _serverTask = Program.MainAsync([], new XunitLoggerProvider(_delegatingTestOutputHelper), _inMemoryTransport, _stopCts.Token);
    }

    public HttpClient HttpClient { get; }

    public Task<McpClient> ConnectMcpClientAsync(McpClientOptions? options, ILoggerFactory loggerFactory)
    {
        return McpClient.CreateAsync(
            new HttpClientTransport(DefaultTransportOptions, HttpClient, loggerFactory),
            options,
            loggerFactory,
            TestContext.Current.CancellationToken);
    }

    public void Initialize(ITestOutputHelper output, HttpClientTransportOptions clientTransportOptions)
    {
        _delegatingTestOutputHelper.CurrentTestOutputHelper = output;
        DefaultTransportOptions = clientTransportOptions;
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
