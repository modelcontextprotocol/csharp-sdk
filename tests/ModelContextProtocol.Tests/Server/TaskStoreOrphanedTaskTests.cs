using ModelContextProtocol.Extensions.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Runtime.InteropServices;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Verifies that when both <see cref="McpTasksBuilderExtensions.WithTasks(IMcpServerBuilder, IMcpTaskStore)"/> and
/// <see cref="McpServerHandlers.CallToolWithAlternateHandler"/> are configured, server creation fails before
/// either alternate-result mechanism can create a task.
/// </summary>
public class TaskStoreOrphanedTaskTests : ClientServerTestBase
{
#pragma warning disable MCPEXP002 // exercises the experimental CallToolWithAlternateHandler/ResultOrAlternate seam
    public TaskStoreOrphanedTaskTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper, startServer: false)
    {
#if !NET
        Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "https://github.com/modelcontextprotocol/csharp-sdk/issues/587");
#endif
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithTasks(new InMemoryMcpTaskStore());

        mcpServerBuilder.Services.Configure<McpServerOptions>(options =>
        {
            options.Capabilities ??= new ServerCapabilities();

            options.Handlers.CallToolWithAlternateHandler = static (context, cancellationToken) =>
                new ValueTask<ResultOrAlternate<CallToolResult>>(new CallToolResult());
        });
    }

    [Fact]
    public void TaskStoreAndExplicitAlternateHandler_ThrowsActionableStartupError()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => StartServer());

        Assert.Contains(nameof(McpServerHandlers.CallToolWithAlternateHandler), exception.Message);
        Assert.Contains(RequestMethods.ToolsCall, exception.Message);
        Assert.Contains("alternate-result filter", exception.Message);
        Assert.Contains("replaces", exception.Message);
    }
#pragma warning restore MCPEXP002
}
