using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests;

public class CancellationTests : ClientServerTestBase
{
    public CancellationTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.AddSingleton(McpServerTool.Create(WaitForCancellation));
    }

    private static async Task WaitForCancellation(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(-1, cancellationToken);
            throw new InvalidOperationException("Unexpected completion without exception");
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }

    [Fact]
    public async Task PrecancelRequest_CancelsBeforeSending()
    {
        await using var client = await CreateMcpClientForServer();

        bool gotCancellation = false;
        await using (Server.RegisterNotificationHandler(NotificationMethods.CancelledNotification, (notification, cancellationToken) =>
        {
            gotCancellation = true;
            return default;
        }))
        {
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await client.ListToolsAsync(cancellationToken: new CancellationToken(true)));
        }

        Assert.False(gotCancellation);
    }

    [Fact]
    public async Task CancellationPropagation_RequestingCancellationCancelsPendingRequest()
    {
        await using var client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var waitTool = tools.First(t => t.Name == "wait_for_cancellation");

        CancellationTokenSource cts = new();
        var waitTask = waitTool.InvokeAsync(cancellationToken: cts.Token);
        Assert.False(waitTask.IsCompleted);

        await Task.Delay(1, TestContext.Current.CancellationToken);
        Assert.False(waitTask.IsCompleted);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waitTask);
    }
}
