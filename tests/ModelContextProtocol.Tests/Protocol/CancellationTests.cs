using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;
using System.Text;

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

    [Fact]
    public async Task InitializeTimeout_DoesNotSendCancellationNotification()
    {
        // Arrange: Create a transport where the server never responds, so the client will time out.
        var serverInput = new MemoryStream();
        var serverOutputPipe = new Pipe();

        var clientTransport = new StreamClientTransport(
            serverInput: serverInput,
            serverOutputPipe.Reader.AsStream(),
            LoggerFactory);

        var clientOptions = new McpClientOptions
        {
            InitializationTimeout = TimeSpan.FromMilliseconds(500),
        };

        // Act: Client will send initialize, then time out since no response comes.
        // Per spec, "The initialize request MUST NOT be cancelled by clients",
        // so no cancellation notification should be sent.
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await McpClient.CreateAsync(clientTransport, clientOptions: clientOptions, loggerFactory: LoggerFactory,
                cancellationToken: TestContext.Current.CancellationToken);
        });

        // Assert: Read what was written to serverInput.
        // The only message should be the initialize request, NOT a cancellation notification.
        var content = Encoding.UTF8.GetString(serverInput.ToArray());
        Assert.Contains("\"method\":\"initialize\"", content);
        Assert.DoesNotContain("notifications/cancelled", content);
    }
}
