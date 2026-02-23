using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Configuration;

public class McpServerOptionsPostSetupTests
{
    [Fact]
    public void TaskStore_IsPopulatedFromDI_WhenNotExplicitlySet()
    {
        var services = new ServiceCollection();
        services.AddMcpServer();
        services.AddSingleton<IMcpTaskStore, InMemoryMcpTaskStore>();

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.IsType<InMemoryMcpTaskStore>(options.TaskStore);
    }

    [Fact]
    public void TaskStore_ExplicitOption_TakesPrecedenceOverDI()
    {
        var explicitStore = new InMemoryMcpTaskStore();

        var services = new ServiceCollection();
        services.AddMcpServer(options => options.TaskStore = explicitStore);
        services.AddSingleton<IMcpTaskStore, InMemoryMcpTaskStore>();

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Same(explicitStore, options.TaskStore);
    }

    [Fact]
    public void TaskStore_RemainsNull_WhenNothingIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddMcpServer();

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Null(options.TaskStore);
    }
}
