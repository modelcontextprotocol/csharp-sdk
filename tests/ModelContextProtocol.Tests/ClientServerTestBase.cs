using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;

namespace ModelContextProtocol.Tests;

public abstract class ClientServerTestBase : LoggedTest, IAsyncDisposable
{
    private readonly Pipe _clientToServerPipe = new();
    private readonly Pipe _serverToClientPipe = new();
    private readonly IMcpServerBuilder _builder;
    private readonly CancellationTokenSource _cts;
    private readonly Task _serverTask;

    public ClientServerTestBase(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        ServiceCollection sc = new();
        sc.AddSingleton(LoggerFactory);
        _builder = sc
            .AddMcpServer()
            .WithStreamServerTransport(_clientToServerPipe.Reader.AsStream(), _serverToClientPipe.Writer.AsStream());
        ConfigureServices(sc, _builder);
        ServiceProvider = sc.BuildServiceProvider();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Server = ServiceProvider.GetRequiredService<IMcpServer>();
        _serverTask = Server.RunAsync(_cts.Token);
    }

    protected IMcpServer Server { get; }

    protected IServiceProvider ServiceProvider { get; }

    protected virtual void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
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

    protected async Task<IMcpClient> CreateMcpClientForServer(McpClientOptions? options = null)
    {
        return await McpClientFactory.CreateAsync(
            new StreamClientTransport(
                serverInput: _clientToServerPipe.Writer.AsStream(),
                _serverToClientPipe.Reader.AsStream(),
                LoggerFactory),
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);
    }
}
