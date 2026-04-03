using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Runtime.InteropServices;

namespace ModelContextProtocol.Tests.Server;

public class McpServerLoggingLevelTests
{
    public McpServerLoggingLevelTests()
    {
#if !NET
        Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "https://github.com/modelcontextprotocol/csharp-sdk/issues/587");
#endif
    }

    [Fact]
    public void CanCreateServerWithLoggingLevelHandler()
    {
        var services = new ServiceCollection();

        services.AddMcpServer()
            .WithStreamServerTransport(Stream.Null, Stream.Null)
            .WithSetLoggingLevelHandler(async (ctx, ct) => new EmptyResult());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<McpServer>();
    }

    [Fact]
    public void AddingLoggingLevelHandlerSetsLoggingCapability()
    {
        var services = new ServiceCollection();

        services.AddMcpServer()
            .WithStreamServerTransport(Stream.Null, Stream.Null)
            .WithSetLoggingLevelHandler(async (ctx, ct) => new EmptyResult());

        using var provider = services.BuildServiceProvider();

        var server = provider.GetRequiredService<McpServer>();

        Assert.NotNull(server.ServerOptions.Capabilities?.Logging);
        Assert.NotNull(server.ServerOptions.Handlers.SetLoggingLevelHandler);
    }

    [Fact]
    public void ServerWithoutCallingLoggingLevelHandlerDoesNotSetLoggingCapability()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithStreamServerTransport(Stream.Null, Stream.Null);
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<McpServer>();
        Assert.Null(server.ServerOptions.Capabilities?.Logging);
    }
}
