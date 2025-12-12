using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Tests.Utils;
using ModelContextProtocol.TestSseServer;
using System.Net;

namespace ModelContextProtocol.AspNetCore.Tests;

public abstract class SseServerIntegrationTestFixture : IAsyncDisposable
{
    private readonly KestrelInMemoryTransport _inMemoryTransport = new();
    private readonly Task _serverTask;
    private readonly CancellationTokenSource _stopCts = new();

    private HttpClientTransportOptions DefaultTransportOptions { get; set; } = new()
    {
        Endpoint = new("http://localhost:5000/"),
    };

    protected SseServerIntegrationTestFixture()
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

        _serverTask = Program.MainAsync([], CreateLoggerProvider(), _inMemoryTransport, _stopCts.Token);
    }
    
    protected abstract ILoggerProvider CreateLoggerProvider();

    public HttpClient HttpClient { get; }

    public Task<McpClient> ConnectMcpClientAsync(McpClientOptions? options, ILoggerFactory loggerFactory)
    {
        return McpClient.CreateAsync(
            new HttpClientTransport(DefaultTransportOptions, HttpClient, loggerFactory),
            options,
            loggerFactory,
            TestContext.Current.CancellationToken);
    }

    public virtual void Initialize(ITestOutputHelper output, HttpClientTransportOptions clientTransportOptions)
    {
        DefaultTransportOptions = clientTransportOptions;
    }

    public virtual void TestCompleted()
    {
    }

    public virtual async ValueTask DisposeAsync()
    {
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

/// <summary>
/// SSE server fixture that routes logs to xUnit test output.
/// </summary>
public class SseServerWithXunitLoggerFixture : SseServerIntegrationTestFixture
{
    // XUnit's ITestOutputHelper is created per test, while this fixture is used for
    // multiple tests, so this dispatches the output to the current test.
    private readonly DelegatingTestOutputHelper _delegatingTestOutputHelper = new();

    protected override ILoggerProvider CreateLoggerProvider()
        => new XunitLoggerProvider(_delegatingTestOutputHelper);

    public override void Initialize(ITestOutputHelper output, HttpClientTransportOptions clientTransportOptions)
    {
        _delegatingTestOutputHelper.CurrentTestOutputHelper = output;
        base.Initialize(output, clientTransportOptions);
    }

    public override void TestCompleted()
    {
        _delegatingTestOutputHelper.CurrentTestOutputHelper = null;
        base.TestCompleted();
    }

    public override async ValueTask DisposeAsync()
    {
        _delegatingTestOutputHelper.CurrentTestOutputHelper = null;
        await base.DisposeAsync();
    }
}

/// <summary>
/// Fixture for tests that need to inspect server logs using MockLoggerProvider.
/// Use <see cref="SseServerWithXunitLoggerFixture"/> for tests that just need xUnit output.
/// </summary>
public class SseServerWithMockLoggerFixture : SseServerIntegrationTestFixture
{
    private readonly MockLoggerProvider _mockLoggerProvider = new();

    protected override ILoggerProvider CreateLoggerProvider()
        => _mockLoggerProvider;

    public IEnumerable<(string Category, LogLevel LogLevel, EventId EventId, string Message, Exception? Exception)> ServerLogs 
        => _mockLoggerProvider.LogMessages;
}
