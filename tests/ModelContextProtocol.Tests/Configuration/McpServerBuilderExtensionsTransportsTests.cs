using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol.Transport;
using Moq;
using System.IO.Pipelines;

namespace ModelContextProtocol.Tests.Configuration;

public class McpServerBuilderExtensionsTransportsTests
{
    [Fact]
    public void WithStdioServerTransport_Sets_Transport()
    {
        var services = new ServiceCollection();
        var builder = new Mock<IMcpServerBuilder>();
        builder.SetupGet(b => b.Services).Returns(services);

        builder.Object.WithStdioServerTransport();

        var transportType = services.FirstOrDefault(s => s.ServiceType == typeof(ITransport));
        Assert.NotNull(transportType);
        Assert.Equal(typeof(StdioServerTransport), transportType.ImplementationType);
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
