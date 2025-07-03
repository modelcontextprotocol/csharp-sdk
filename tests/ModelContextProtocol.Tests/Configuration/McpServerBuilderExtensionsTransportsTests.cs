using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.IO.Pipelines;

namespace ModelContextProtocol.Tests.Configuration;

public class McpServerBuilderExtensionsTransportsTests
{
    [Fact]
    public void WithStdioServerTransport_Sets_Transport()
    {
        var services = new ServiceCollection();
        services.AddMcpServer().WithStdioServerTransport();

        var transportServiceType = services.FirstOrDefault(s => s.ServiceType == typeof(ITransport));
        Assert.NotNull(transportServiceType);

        var serviceProvider = services.BuildServiceProvider();
        Assert.IsType<StdioServerTransport>(serviceProvider.GetRequiredService<ITransport>());
    }

    [Fact]
    public async Task HostExecutionShutsDownWhenSingleSessionServerExits()
    {
        Pipe clientToServerPipe = new(), serverToClientPipe = new();

        var builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Services
            .AddMcpServer()
            .WithStreamServerTransport(clientToServerPipe.Reader.AsStream(), serverToClientPipe.Writer.AsStream());

        IHost host = builder.Build();

        Task t = host.RunAsync(TestContext.Current.CancellationToken);
        await Task.Delay(1, TestContext.Current.CancellationToken);
        Assert.False(t.IsCompleted);

        clientToServerPipe.Writer.Complete();
        await t;
    }
}
