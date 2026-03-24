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
    public async Task CanCreateServerWithLoggingLevelHandler()
    {
        var services = new ServiceCollection();

        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithSetLoggingLevelHandler(async (ctx, ct) => new EmptyResult());

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<McpServer>();
    }

    [Fact]
    public async Task AddingLoggingLevelHandlerSetsLoggingCapability()
    {
        var services = new ServiceCollection();

        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithSetLoggingLevelHandler(async (ctx, ct) => new EmptyResult());

        await using var provider = services.BuildServiceProvider();

        var server = provider.GetRequiredService<McpServer>();

        Assert.NotNull(server.ServerOptions.Capabilities?.Logging);
        Assert.NotNull(server.ServerOptions.Handlers.SetLoggingLevelHandler);
    }

    [Fact]
    public async Task ServerWithoutCallingLoggingLevelHandlerDoesNotSetLoggingCapability()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithStdioServerTransport();
        await using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<McpServer>();
        Assert.Null(server.ServerOptions.Capabilities?.Logging);
    }
}
