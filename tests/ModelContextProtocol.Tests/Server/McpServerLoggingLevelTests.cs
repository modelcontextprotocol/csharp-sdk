using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;
public class McpServerLoggingLevelTests
{
    [Fact]
    public void CanCreateServerWithLoggingLevelHandler()
    {
        var services = new ServiceCollection();

        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithSetLoggingLevelHandler((ctx, ct) =>
            {
                return Task.FromResult(new EmptyResult());
            });

        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IMcpServer>();
    }

    [Fact]
    public void AddingLoggingLevelHandlerSetsLoggingCapability()
    {
        var services = new ServiceCollection();

        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithSetLoggingLevelHandler((ctx, ct) =>
            {
                return Task.FromResult(new EmptyResult());
            });

        var provider = services.BuildServiceProvider();

        var server = provider.GetRequiredService<IMcpServer>();

        Assert.NotNull(server.ServerOptions.Capabilities?.Logging);
        Assert.NotNull(server.ServerOptions.Capabilities.Logging.SetLoggingLevelHandler);
    }

    [Fact]
    public void ServerWithoutCallingLoggingLevelHandlerDoesNotSetLoggingCapability()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithStdioServerTransport();
        var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<IMcpServer>();
        Assert.Null(server.ServerOptions.Capabilities?.Logging);
    }
}
