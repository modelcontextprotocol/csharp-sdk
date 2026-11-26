using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Tests;

public class CancellationTests : ClientServerTestBase
{
    private readonly TaskCompletionSource<bool> _toolWaiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _toolCanceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _toolCallHandled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CancellationTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.AddSingleton(McpServerTool.Create(WaitForCancellation));
        services.AddSingleton(McpServerTool.Create(SignalThenWaitForCancellation, new() { Name = "signal_then_wait_for_cancellation" }));
        services.AddSingleton(McpServerTool.Create(SignalThenReturnOnCancellation, new() { Name = "signal_then_return_on_cancellation" }));
        services.AddSingleton(McpServerPrompt.Create(ThrowCanceledWithoutCancellation, new() { Name = "throw_canceled_without_cancellation" }));
        services.AddSingleton(McpServerTool.Create(ElicitThenWaitForCancellation, new() { Name = "elicit_then_wait_for_cancellation" }));

        // Incoming message filters wrap request handling, including its outcome logging,
        // so this signals once the server has finished handling and logging a tools/call request.
        mcpServerBuilder.WithMessageFilters(filters => filters.AddIncomingFilter(next => async (context, cancellationToken) =>
        {
            try
            {
                await next(context, cancellationToken);
            }
            finally
            {
                if (context.JsonRpcMessage is JsonRpcRequest { Method: RequestMethods.ToolsCall })
                {
                    _toolCallHandled.TrySetResult(true);
                }
            }
        }));
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

    private async Task SignalThenWaitForCancellation(CancellationToken cancellationToken)
    {
        _toolWaiting.TrySetResult(true);
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _toolCanceled.TrySetResult(true);
            throw;
        }
    }

    private async Task ElicitThenWaitForCancellation(McpServer server, CancellationToken cancellationToken)
    {
        // On protocol versions that predate MRTR, this server-to-client request is sent over the wire with an ID
        // allocated by the server, independently of the IDs the client allocates for its own requests.
        await server.ElicitAsync(new ElicitRequestParams { Message = "Continue?", RequestedSchema = new() }, cancellationToken);

        await SignalThenWaitForCancellation(cancellationToken);
    }

    private async Task<string> SignalThenReturnOnCancellation(CancellationToken cancellationToken)
    {
        // Like a subscriptions/listen handler: wait until the request is canceled, then return normally.
        // The OperationCanceledException then comes from the SDK sending the response with the canceled token.
        _toolWaiting.TrySetResult(true);
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _toolCanceled.TrySetResult(true);
        }

        return "done";
    }

    private static string ThrowCanceledWithoutCancellation() =>
        throw new OperationCanceledException("Canceled by the handler itself, not by the request.");

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

    [Theory]
    [InlineData(null)]
    [InlineData("2025-11-25")]
    public async Task CancellationPropagation_CancelingRequest_CancelsServerHandler(string? protocolVersion)
    {
        await using var client = await CreateMcpClientForServer(new McpClientOptions { ProtocolVersion = protocolVersion });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var callTask = client.CallToolAsync("signal_then_wait_for_cancellation", cancellationToken: cts.Token).AsTask();

        await _toolWaiting.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await callTask);

        // The client must send notifications/cancelled, which in turn cancels the server-side handler.
        await _toolCanceled.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CancellationPropagation_ResponseWithSameIdAsInFlightRequest_DoesNotPreventCancellation()
    {
        await using var client = await CreateMcpClientForServer(new McpClientOptions
        {
            // Pin to a legacy protocol version so ElicitAsync is sent as a JSON-RPC request rather than via MRTR.
            ProtocolVersion = "2025-11-25",
            Handlers = new()
            {
                ElicitationHandler = (request, cancellationToken) => new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" }),
            },
        });

        // Each side allocates its own request IDs starting at 1, so the server's elicitation/create request
        // (its first outgoing request) is assigned ID 1. Give the client's tools/call request the same ID.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var callTask = client.SendRequestAsync(new JsonRpcRequest
        {
            Id = new RequestId(1),
            Method = RequestMethods.ToolsCall,
            Params = JsonSerializer.SerializeToNode(
                new CallToolRequestParams { Name = "elicit_then_wait_for_cancellation" },
                McpJsonUtilities.DefaultOptions),
        }, cts.Token);

        // By the time the tool is waiting, the server has received the elicitation response with ID 1.
        await _toolWaiting.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await callTask);

        // The notifications/cancelled for the in-flight tools/call must still reach its handler.
        await _toolCanceled.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("signal_then_wait_for_cancellation")]
    [InlineData("signal_then_return_on_cancellation")]
    public async Task CancelingRequest_LogsHandlerCanceled_NotFailed(string toolName)
    {
        await using var client = await CreateMcpClientForServer();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var callTask = client.CallToolAsync(toolName, cancellationToken: cts.Token).AsTask();

        await _toolWaiting.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await callTask);
        await _toolCallHandled.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        var canceledLog = Assert.Single(MockLoggerProvider.LogMessages, m => m.Message.Contains("'tools/call' request handler canceled"));
        Assert.Equal(LogLevel.Information, canceledLog.LogLevel);
        Assert.DoesNotContain(MockLoggerProvider.LogMessages, m => m.Message.Contains("'tools/call' request handler failed"));
    }

    [Fact]
    public async Task HandlerThrowingOperationCanceled_WithoutRequestCancellation_LogsHandlerFailed()
    {
        await using var client = await CreateMcpClientForServer();

        // The request wasn't canceled, so the server reports an error instead of treating it as a cancellation.
        // (A prompt is used because tool exceptions are converted into error results rather than propagated.)
        await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.GetPromptAsync("throw_canceled_without_cancellation", cancellationToken: TestContext.Current.CancellationToken));

        var failedLog = Assert.Single(MockLoggerProvider.LogMessages, m => m.Message.Contains("'prompts/get' request handler failed"));
        Assert.Equal(LogLevel.Warning, failedLog.LogLevel);
        Assert.IsAssignableFrom<OperationCanceledException>(failedLog.Exception);
        Assert.DoesNotContain(MockLoggerProvider.LogMessages, m => m.Message.Contains("request handler canceled"));
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
            // Pin to a legacy protocol version so the client performs the initialize handshake
            // (the spec rule under test is "the initialize request MUST NOT be cancelled by clients").
            ProtocolVersion = "2025-11-25",
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
