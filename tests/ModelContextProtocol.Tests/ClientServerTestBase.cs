using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;

namespace ModelContextProtocol.Tests;

public abstract class ClientServerTestBase : LoggedTest, IAsyncDisposable
{
    private readonly Pipe _clientToServerPipe = new();
    private readonly Pipe _serverToClientPipe = new();
    private readonly CancellationTokenSource _cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    private Task _serverTask = Task.CompletedTask;

    public ClientServerTestBase(ITestOutputHelper testOutputHelper, bool startServer = true)
        : base(testOutputHelper)
    {
        ServiceCollection.AddLogging();
        ServiceCollection.AddSingleton(XunitLoggerProvider);
        ServiceCollection.AddSingleton<ILoggerProvider>(MockLoggerProvider);
        McpServerBuilder = ServiceCollection
            .AddMcpServer()
            .WithStreamServerTransport(_clientToServerPipe.Reader.AsStream(), _serverToClientPipe.Writer.AsStream());

        ConfigureServices(ServiceCollection, McpServerBuilder);

        if (startServer)
        {
            StartServer();
        }
    }

    protected ServiceCollection ServiceCollection { get; } = [];

    protected IMcpServerBuilder McpServerBuilder { get; }

    protected McpServer Server
    {
         get => field ?? throw new InvalidOperationException("You must call StartServer first.");
         private set => field = value;
    }

    protected ServiceProvider ServiceProvider
    {
         get => field ?? throw new InvalidOperationException("You must call StartServer first.");
         private set => field = value;
    }

    protected virtual void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
    }

    protected McpServer StartServer()
    {
        ServiceProvider = ServiceCollection.BuildServiceProvider(validateScopes: true);
        Server = ServiceProvider.GetRequiredService<McpServer>();
        _serverTask = Server.RunAsync(_cts.Token);
        return Server;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        _clientToServerPipe.Writer.Complete();
        _serverToClientPipe.Writer.Complete();

        await _serverTask;

        if (ServiceProvider is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _cts.Dispose();
        Dispose();
    }

    protected async Task<McpClient> CreateMcpClientForServer(McpClientOptions? clientOptions = null)
    {
        return await McpClient.CreateAsync(
            new StreamClientTransport(
                serverInput: _clientToServerPipe.Writer.AsStream(),
                _serverToClientPipe.Reader.AsStream(),
                LoggerFactory),
            clientOptions: clientOptions,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);
    }
}
