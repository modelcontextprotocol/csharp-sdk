using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.IO.Pipelines;

namespace ModelContextProtocol.Tests.Configuration;

public class McpServerBuilderExtensionsTransportsTests
{
    [Fact]
    public void WithStdioServerTransport_Registers_Transport()
    {
        var services = new ServiceCollection();
        services.AddMcpServer().WithStdioServerTransport();

        // Verify StdioServerTransport is registered for ITransport, but don't resolve it —
        // doing so opens Console.OpenStandardInput() which permanently blocks a thread pool
        // thread on the test host's stdin. StdioServerTransport should only be used in a
        // dedicated child process, not in-process.
        var transportDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(ITransport));
        Assert.NotNull(transportDescriptor);
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
