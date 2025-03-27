using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Configuration;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Transport;
using System.IO.Pipelines;

namespace ModelContextProtocol.Tests.Client;

public class McpClientExtensionsTests
{
    private Pipe _clientToServerPipe = new();
    private Pipe _serverToClientPipe = new();
    private readonly IMcpServer _server;

    public McpClientExtensionsTests()
    {
        ServiceCollection sc = new();
        sc.AddSingleton<IServerTransport>(new StdioServerTransport("TestServer", _clientToServerPipe.Reader.AsStream(), _serverToClientPipe.Writer.AsStream()));
        sc.AddMcpServer();
        for (int f = 0; f < 10; f++)
        {
            string name = $"Method{f}";
            sc.AddSingleton(McpServerTool.Create((int i) => $"{name} Result {i}", name));
        }
        _server = sc.BuildServiceProvider().GetRequiredService<IMcpServer>();
    }

    public ValueTask DisposeAsync()
    {
        _clientToServerPipe.Writer.Complete();
        _serverToClientPipe.Writer.Complete();
        return _server.DisposeAsync();
    }

    private async Task<IMcpClient> CreateMcpClientForServer()
    {
        await _server.StartAsync(TestContext.Current.CancellationToken);

        var stdin = new StreamWriter(_clientToServerPipe.Writer.AsStream());
        var stdout = new StreamReader(_serverToClientPipe.Reader.AsStream());

        var serverConfig = new McpServerConfig()
        {
            Id = "TestServer",
            Name = "TestServer",
            TransportType = "ignored",
        };

        return await McpClientFactory.CreateAsync(
            serverConfig,
            createTransportFunc: (_, _) => new StreamClientTransport(stdin, stdout),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ListToolsAsync_AllToolsReturned()
    {
        IMcpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(10, tools.Count);
        var echo = tools.Single(t => t.Name == "Method4");
        var result = await echo.InvokeAsync(new Dictionary<string, object?>() { ["i"] = 42 }, TestContext.Current.CancellationToken);
        Assert.Contains("Method4 Result 42", result?.ToString());
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