using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;

namespace ModelContextProtocol.Tests.Client;

public class McpClientExtensionsTests : LoggedTest
{
    private readonly Pipe _clientToServerPipe = new();
    private readonly Pipe _serverToClientPipe = new();
    private readonly ServiceProvider _serviceProvider;
    private readonly CancellationTokenSource _cts;
    private readonly Task _serverTask;

    public McpClientExtensionsTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
        ServiceCollection sc = new();
        sc.AddSingleton(LoggerFactory);
        sc.AddMcpServer().WithStdioServerTransport();
        // Call WithStdioServerTransport to get the IMcpServer registration, then overwrite default transport with a pipe transport.
        sc.AddSingleton<ITransport>(new StreamServerTransport(_clientToServerPipe.Reader.AsStream(), _serverToClientPipe.Writer.AsStream()));
        for (int f = 0; f < 10; f++)
        {
            string name = $"Method{f}";
            sc.AddSingleton(McpServerTool.Create((int i) => $"{name} Result {i}", new() { Name = name }));
        }
        sc.AddSingleton(McpServerTool.Create([McpServerTool(Destructive = false, OpenWorld = true)](string i) => $"{i} Result", new() { Name = "ValuesSetViaAttr" }));
        sc.AddSingleton(McpServerTool.Create([McpServerTool(Destructive = false, OpenWorld = true)](string i) => $"{i} Result", new() { Name = "ValuesSetViaOptions", Destructive = true, OpenWorld = false, ReadOnly = true }));
        _serviceProvider = sc.BuildServiceProvider();

        var server = _serviceProvider.GetRequiredService<IMcpServer>();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _serverTask = server.RunAsync(cancellationToken: _cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        _clientToServerPipe.Writer.Complete();
        _serverToClientPipe.Writer.Complete();

        await _serverTask;

        await _serviceProvider.DisposeAsync();
        _cts.Dispose();
    }

    private async Task<IMcpClient> CreateMcpClientForServer()
    {
        return await McpClientFactory.CreateAsync(
            new McpServerConfig()
            {
                Id = "TestServer",
                Name = "TestServer",
                TransportType = "ignored",
            },
            createTransportFunc: (_, _) => new StreamClientTransport(
                serverInput: _clientToServerPipe.Writer.AsStream(),
                serverOutput: _serverToClientPipe.Reader.AsStream(),
                LoggerFactory),
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ListToolsAsync_AllToolsReturned()
    {
        IMcpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(12, tools.Count);
        var echo = tools.Single(t => t.Name == "Method4");
        var result = await echo.InvokeAsync(new Dictionary<string, object?>() { ["i"] = 42 }, TestContext.Current.CancellationToken);
        Assert.Contains("Method4 Result 42", result?.ToString());

        var valuesSetViaAttr = tools.Single(t => t.Name == "ValuesSetViaAttr");
        Assert.Null(valuesSetViaAttr.ProtocolTool.Annotations?.Title);
        Assert.Null(valuesSetViaAttr.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.Null(valuesSetViaAttr.ProtocolTool.Annotations?.IdempotentHint);
        Assert.False(valuesSetViaAttr.ProtocolTool.Annotations?.DestructiveHint);
        Assert.True(valuesSetViaAttr.ProtocolTool.Annotations?.OpenWorldHint);

        var valuesSetViaOptions = tools.Single(t => t.Name == "ValuesSetViaOptions");
        Assert.Null(valuesSetViaOptions.ProtocolTool.Annotations?.Title);
        Assert.True(valuesSetViaOptions.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.Null(valuesSetViaOptions.ProtocolTool.Annotations?.IdempotentHint);
        Assert.True(valuesSetViaOptions.ProtocolTool.Annotations?.DestructiveHint);
        Assert.False(valuesSetViaOptions.ProtocolTool.Annotations?.OpenWorldHint);
    }

    [Fact]
    public async Task EnumerateToolsAsync_AllToolsReturned()
    {
        IMcpClient client = await CreateMcpClientForServer();

        await foreach (var tool in client.EnumerateToolsAsync(TestContext.Current.CancellationToken))
        {
            if (tool.Name == "Method4")
            {
                var result = await tool.InvokeAsync(new Dictionary<string, object?>() { ["i"] = 42 }, TestContext.Current.CancellationToken);
                Assert.Contains("Method4 Result 42", result?.ToString());
                return;
            }
        }

        Assert.Fail("Couldn't find target method");
    }
}