using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;

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

    [Fact]
    public async Task WithStdioServerTransport_With_Options_Configures_Transport_Options()
    {
        var services = new ServiceCollection();
        services.AddMcpServer().WithStdioServerTransport(options => options.MaxMessageSize = 1234);

        // As above, don't resolve the stdio transport itself.
        Assert.Single(services, s => s.ServiceType == typeof(ITransport));

        await using var serviceProvider = services.BuildServiceProvider();
        Assert.Equal(1234, serviceProvider.GetRequiredService<IOptions<StreamServerTransportOptions>>().Value.MaxMessageSize);
    }

    [Fact]
    public async Task WithStreamServerTransport_With_Options_Applies_MaxMessageSize()
    {
        Pipe clientToServerPipe = new(), serverToClientPipe = new();

        var services = new ServiceCollection();
        services.AddMcpServer().WithStreamServerTransport(
            clientToServerPipe.Reader.AsStream(),
            serverToClientPipe.Writer.AsStream(),
            options => options.MaxMessageSize = 64);

        await using var serviceProvider = services.BuildServiceProvider();
        _ = serviceProvider.GetRequiredService<ITransport>();

        string request = $$"""{"jsonrpc":"2.0","method":"ping","params":{"padding":"{{new string('x', 100)}}"},"id":3}""";
        await clientToServerPipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(request + "\n"), TestContext.Current.CancellationToken);

        using var responseReader = new StreamReader(serverToClientPipe.Reader.AsStream(), Encoding.UTF8);
        var responseLine = await responseReader.ReadLineAsync(
#if NET
            TestContext.Current.CancellationToken
#endif
        );

        var error = Assert.IsType<JsonRpcError>(JsonSerializer.Deserialize<JsonRpcMessage>(responseLine!, McpJsonUtilities.DefaultOptions));
        Assert.Equal(new RequestId(3), error.Id);
        Assert.Equal((int)McpErrorCode.InvalidRequest, error.Error.Code);

        clientToServerPipe.Writer.Complete();
    }
}
