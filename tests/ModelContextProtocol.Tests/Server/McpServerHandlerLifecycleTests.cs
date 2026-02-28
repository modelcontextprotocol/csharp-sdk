using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

public class McpServerHandlerLifecycleTests : ClientServerTestBase
{
    public McpServerHandlerLifecycleTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithTools<SlowTool>();
        services.AddScoped<TrackedService>();
    }

    [Fact]
    public async Task ScopedServicesAreAccessibleThroughoutHandlerLifetime_EvenDuringShutdown()
    {
        // Arrange: create client and call the slow tool
        await using McpClient client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.First(t => t.Name == "slow_tool");

        TrackedService.Reset();

        // Act: invoke the tool which delays, then accesses the scoped service
        CallToolResult result = await tool.CallAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the scoped service was successfully accessed after the delay.
        // If the handler task were not awaited during shutdown, the service scope could
        // be disposed before the handler finishes, causing ObjectDisposedException.
        Assert.Equal(1, TrackedService.TotalConstructed);
        var textContent = Assert.IsType<TextContentBlock>(result.Content.First());
        Assert.Contains("service-ok", textContent.Text);
    }

    [McpServerToolType]
    public sealed class SlowTool
    {
        [McpServerTool]
        public static async Task<string> SlowToolAsync(TrackedService service, CancellationToken cancellationToken)
        {
            // Simulate a handler that takes some time, then accesses a scoped service.
            await Task.Delay(100, cancellationToken);

            // Access the scoped service after the delay. If the scope were disposed
            // prematurely, this would throw ObjectDisposedException.
            service.DoWork();

            return "service-ok";
        }
    }

    public class TrackedService : IAsyncDisposable
    {
        private static int s_totalConstructed;
        private static int s_totalDisposed;
        private bool _disposed;

        public TrackedService()
        {
            Interlocked.Increment(ref s_totalConstructed);
        }

        public static int TotalConstructed => Volatile.Read(ref s_totalConstructed);
        public static int TotalDisposed => Volatile.Read(ref s_totalDisposed);

        public static void Reset()
        {
            Interlocked.Exchange(ref s_totalConstructed, 0);
            Interlocked.Exchange(ref s_totalDisposed, 0);
        }

        public void DoWork()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TrackedService));
            }
        }

        public ValueTask DisposeAsync()
        {
            _disposed = true;
            Interlocked.Increment(ref s_totalDisposed);
            return default;
        }
    }
}
