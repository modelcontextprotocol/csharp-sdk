using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for the server's MRTR handler lifecycle management — cancellation, disposal, and error
/// logging during multi round-trip request processing.
/// </summary>
public class MrtrHandlerLifecycleTests : ClientServerTestBase
{
    private readonly TaskCompletionSource<bool> _handlerTokenCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _handlerStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _handlerResumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _releaseHandler = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ServerMessageTracker _tracker = new();

    public MrtrHandlerLifecycleTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
        services.Configure<McpServerOptions>(options =>
        {
            options.ExperimentalProtocolVersion = "2026-06-XX";
            _tracker.AddOutgoingFilter(options.Filters.Message);
        });

        mcpServerBuilder.WithTools([
            McpServerTool.Create(
                async (string message, McpServer server, CancellationToken ct) =>
                {
                    var result = await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = message,
                        RequestedSchema = new()
                    }, ct);

                    return $"{result.Action}:{result.Content?.FirstOrDefault().Value}";
                },
                new McpServerToolCreateOptions
                {
                    Name = "elicitation-tool",
                    Description = "A tool that requests elicitation from the client"
                }),
            McpServerTool.Create(
                async (McpServer server, CancellationToken ct) =>
                {
                    var handlerTokenCancelled = _handlerTokenCancelled;
                    ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), handlerTokenCancelled);
                    _handlerStarted.TrySetResult(true);

                    await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = "Cancellation test",
                        RequestedSchema = new()
                    }, ct);

                    return "done";
                },
                new McpServerToolCreateOptions
                {
                    Name = "cancellation-test-tool",
                    Description = "A tool that monitors its CancellationToken during MRTR"
                }),
            McpServerTool.Create(
                async (string message, McpServer server, CancellationToken ct) =>
                {
                    // Elicit first, then block forever — the retry request stays in-flight
                    // until the client cancels, verifying that notifications/cancelled for
                    // the retry's request ID flows through to cancel this handler.
                    _handlerStarted.TrySetResult(true);
                    var result = await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = message,
                        RequestedSchema = new()
                    }, ct);

                    // Signal that we resumed after ElicitAsync, then block.
                    _handlerResumed.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, ct);
                    return "unreachable";
                },
                new McpServerToolCreateOptions
                {
                    Name = "elicit-then-block-tool",
                    Description = "A tool that elicits then blocks forever for cancellation testing"
                }),
            McpServerTool.Create(
                async (McpServer server, CancellationToken ct) =>
                {
                    // Two sequential MRTR rounds. The client will inject a stale cancellation
                    // notification for the original request ID between round 1 and round 2.
                    var r1 = await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = "First elicitation",
                        RequestedSchema = new()
                    }, ct);

                    // Signal that round 1 completed so the test can inject the stale notification.
                    _handlerResumed.TrySetResult(true);

                    var r2 = await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = "Second elicitation",
                        RequestedSchema = new()
                    }, ct);

                    return $"{r1.Action},{r2.Action}";
                },
                new McpServerToolCreateOptions
                {
                    Name = "double-elicit-tool",
                    Description = "A tool that elicits twice for stale cancellation testing"
                }),
            McpServerTool.Create(
                async (string message, McpServer server, CancellationToken ct) =>
                {
                    // Elicit, resume, then wait on _releaseHandler for the dispose test.
                    _handlerStarted.TrySetResult(true);
                    await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = message,
                        RequestedSchema = new()
                    }, ct);

                    _handlerResumed.TrySetResult(true);
                    await _releaseHandler.Task;
                    return "handler-completed";
                },
                new McpServerToolCreateOptions
                {
                    Name = "dispose-wait-tool",
                    Description = "A tool that elicits, resumes, then waits on a signal for disposal testing"
                }),
            McpServerTool.Create(
                async (McpServer server, CancellationToken ct) =>
                {
                    await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = "elicit-then-throw",
                        RequestedSchema = new()
                    }, ct);

                    throw new InvalidOperationException("Deliberate MRTR handler error for testing");
                },
                new McpServerToolCreateOptions
                {
                    Name = "elicit-then-throw-tool",
                    Description = "A tool that elicits then throws an exception for error logging testing"
                }),
            McpServerTool.Create(
                (McpServer server) =>
                {
                    // Low-level MRTR: throw IncompleteResultException directly instead of using ElicitAsync.
                    // This should NOT be logged at Error level — it's normal MRTR control flow.
                    throw new IncompleteResultException(new IncompleteResult
                    {
                        InputRequests = new Dictionary<string, InputRequest>
                        {
                            ["input_1"] = InputRequest.ForElicitation(new ElicitRequestParams
                            {
                                Message = "low-level elicit",
                                RequestedSchema = new()
                            })
                        }
                    });
                },
                new McpServerToolCreateOptions
                {
                    Name = "incomplete-result-tool",
                    Description = "A tool that throws IncompleteResultException for low-level MRTR"
                })
        ]);
    }

    [Fact]
    public async Task CallToolAsync_CancellationDuringMrtrRetry_ThrowsOperationCanceled()
    {
        // Verify that cancelling the CancellationToken during the MRTR retry loop
        // (specifically during the elicitation handler callback) stops the loop.
        StartServer();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            // Cancel the token during the callback. The retry loop will throw
            // OperationCanceledException on the next await after this handler returns.
            cts.Cancel();
            return new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await client.CallToolAsync("elicitation-tool",
                new Dictionary<string, object?> { ["message"] = "test" },
                cancellationToken: cts.Token));

        _tracker.AssertNoLegacyMrtrRequests();
    }

    [Fact]
    public async Task ServerDisposal_CancelsHandlerCancellationToken_DuringMrtr()
    {
        // Verify that disposing the server cancels the handler's own CancellationToken
        // (the `ct` parameter), not just the exchange ResponseTcs. Before the HandlerCts fix,
        // the handler's CT was from a disposed CTS and could never be triggered.
        StartServer();
        var elicitHandlerCalled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = async (request, ct) =>
        {
            // Signal that the MRTR round trip reached the client, then block indefinitely.
            elicitHandlerCalled.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, ct);
            throw new OperationCanceledException(ct);
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        // Start the tool call in the background.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        var callTask = client.CallToolAsync("cancellation-test-tool", cancellationToken: cts.Token).AsTask();

        // Wait for the handler to start on the server.
        await _handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Wait for the MRTR round trip to reach the client's elicitation handler.
        await elicitHandlerCalled.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Dispose the server — HandlerCts.Cancel() should trigger the handler's CancellationToken.
        await Server.DisposeAsync();

        // Verify the handler's CancellationToken was actually cancelled via HandlerCts,
        // not just the exchange ResponseTcs.TrySetCanceled().
        await _handlerTokenCancelled.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // The client call should fail (server disposed mid-MRTR).
        await Assert.ThrowsAnyAsync<Exception>(async () => await callTask);

        _tracker.AssertNoLegacyMrtrRequests();
    }

    [Fact]
    public async Task CancellationNotification_DuringInFlightMrtrRetry_CancelsHandler()
    {
        // Verify that cancelling the client's CancellationToken while a retry request is in-flight
        // sends notifications/cancelled with the retry's request ID, and the server correctly
        // routes it to cancel the handler. This proves end-to-end that:
        // (a) the client sends the notification with the CURRENT request ID (not the original),
        // (b) the server's _handlingRequests lookup finds the retry's CTS,
        // (c) the cancellation registration in AwaitMrtrHandlerAsync bridges to handlerCts.
        StartServer();

        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
            new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });

        await using var client = await CreateMcpClientForServer(clientOptions);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        var callTask = client.CallToolAsync(
            "elicit-then-block-tool",
            new Dictionary<string, object?> { ["message"] = "test" },
            cancellationToken: cts.Token).AsTask();

        // Wait for the handler to resume after ElicitAsync — at this point the retry
        // request is in-flight (server is awaiting WhenAny in AwaitMrtrHandlerAsync).
        await _handlerResumed.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Cancel the client's token. The client is inside _sessionHandler.SendRequestAsync
        // awaiting the retry response. RegisterCancellation fires and sends
        // notifications/cancelled with the retry's request ID.
        cts.Cancel();

        // The call should throw OperationCanceledException.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await callTask);

        _tracker.AssertNoLegacyMrtrRequests();
    }

    [Fact]
    public async Task CancellationNotification_ForExpiredRequestId_DoesNotAffectHandler()
    {
        // Verify that a stale cancellation notification for the original (now-completed)
        // request ID does not interfere with an active MRTR handler. The original request's
        // entry was removed from _handlingRequests when it returned IncompleteResult, so
        // the notification should be a no-op.
        StartServer();

        int elicitationCount = 0;
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            Interlocked.Increment(ref elicitationCount);
            return new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        // Start the double-elicit tool. Between round 1 and round 2, we'll inject a stale
        // cancellation notification for a fake (expired) request ID.
        var callTask = client.CallToolAsync(
            "double-elicit-tool",
            cancellationToken: TestContext.Current.CancellationToken).AsTask();

        // Wait for handler to resume after the first ElicitAsync.
        await _handlerResumed.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Send a stale cancellation notification for a non-existent request ID.
        // This simulates a delayed notification for the original request that already completed.
        await client.SendMessageAsync(new JsonRpcNotification
        {
            Method = NotificationMethods.CancelledNotification,
            Params = JsonSerializer.SerializeToNode(
                new CancelledNotificationParams { RequestId = new RequestId("stale-id-999"), Reason = "stale test" },
                McpJsonUtilities.DefaultOptions),
        }, TestContext.Current.CancellationToken);

        // The tool should complete successfully — the stale notification didn't affect it.
        var result = await callTask;
        Assert.Contains("accept", result.Content.OfType<TextContentBlock>().First().Text);

        _tracker.AssertNoLegacyMrtrRequests();
    }

    [Fact]
    public async Task DisposeAsync_WaitsForMrtrHandler_BeforeReturning()
    {
        // Verify that McpServer.DisposeAsync() waits for an MRTR handler to complete
        // before returning, similar to RunAsync_WaitsForInFlightHandlersBeforeReturning
        // which tests the same invariant for regular request handlers in McpSessionHandler.
        StartServer();
        bool handlerCompleted = false;

        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
            new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });

        await using var client = await CreateMcpClientForServer(clientOptions);

        // Start the tool call that calls ElicitAsync, then blocks on _releaseHandler.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        _ = client.CallToolAsync(
            "dispose-wait-tool",
            new Dictionary<string, object?> { ["message"] = "dispose-wait-test" },
            cancellationToken: cts.Token);

        // Wait for the handler to resume after ElicitAsync — it's now blocking on _releaseHandler.
        await _handlerResumed.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Dispose the server. The handler is still running (blocked on _releaseHandler).
        // Release the handler after a delay — DisposeAsync must wait for it.
        var ct = TestContext.Current.CancellationToken;
        _ = Task.Run(async () =>
        {
            await Task.Delay(200, ct);
            handlerCompleted = true;
            _releaseHandler.SetResult(true);
        }, ct);

        await Server.DisposeAsync();

        // DisposeAsync should not have returned until the handler completed.
        Assert.True(handlerCompleted, "DisposeAsync should wait for MRTR handlers to complete before returning.");

        _tracker.AssertNoLegacyMrtrRequests();
    }

    [Fact]
    public async Task HandlerException_DuringMrtr_IsLoggedAtErrorLevel()
    {
        // Verify that when a tool handler throws an unhandled exception during MRTR
        // (after resuming from ElicitAsync), the error is logged at Error level.
        StartServer();

        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
            new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });

        await using var client = await CreateMcpClientForServer(clientOptions);

        // Call the tool that elicits then throws. The retry returns an error result.
        var result = await client.CallToolAsync(
            "elicit-then-throw-tool",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsError);

        // Verify the tool error was logged at Error level during the MRTR retry.
        // The ToolsCall handler catches the exception, logs it via ToolCallError,
        // and converts it to an error result — so the error is properly surfaced.
        Assert.Contains(MockLoggerProvider.LogMessages, m =>
            m.LogLevel == LogLevel.Error &&
            m.Message.Contains("elicit-then-throw-tool") &&
            m.Exception is InvalidOperationException);

        _tracker.AssertNoLegacyMrtrRequests();
    }

    [Fact]
    public async Task IncompleteResultException_IsNotLoggedAtErrorLevel()
    {
        // IncompleteResultException is normal MRTR control flow (low-level API),
        // not an error. It should not be logged via ToolCallError at Error level.
        StartServer();

        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
            new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });

        await using var client = await CreateMcpClientForServer(clientOptions);

        // The tool always throws IncompleteResultException (low-level MRTR path),
        // so the client will retry until hitting the max retry limit.
        await Assert.ThrowsAsync<McpException>(() => client.CallToolAsync(
            "incomplete-result-tool",
            cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.DoesNotContain(MockLoggerProvider.LogMessages, m =>
            m.LogLevel == LogLevel.Error &&
            m.Exception is IncompleteResultException);

        _tracker.AssertNoLegacyMrtrRequests();
    }
}
