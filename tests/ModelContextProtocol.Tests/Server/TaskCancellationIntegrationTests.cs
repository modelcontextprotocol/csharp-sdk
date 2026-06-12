using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Runtime.InteropServices;
using System.Text.Json;

#pragma warning disable MCPEXP001

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Integration tests for task cancellation behavior, including explicit cancellation
/// via tasks/cancel and TTL-based automatic cancellation.
/// </summary>
public class TaskCancellationIntegrationTests : ClientServerTestBase
{
    private readonly TaskCompletionSource<bool> _toolCancellationFired = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _toolStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCancellationIntegrationTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
#if !NET
        Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "https://github.com/modelcontextprotocol/csharp-sdk/issues/587");
#endif
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.Services.Configure<McpServerOptions>(options =>
        {
            options.TaskStore = new InMemoryMcpTaskStore
            {
                DefaultPollIntervalMs = 50,
                DefaultTimeToLive = TimeSpan.FromSeconds(5),
            };
        });

        mcpServerBuilder.WithTools([McpServerTool.Create(
            async (CancellationToken ct) =>
            {
                _toolStarted.TrySetResult(true);
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                    return "completed";
                }
                catch (OperationCanceledException)
                {
                    _toolCancellationFired.TrySetResult(true);
                    throw;
                }
            },
            new McpServerToolCreateOptions
            {
                Name = "long-running-tool",
                Description = "A tool that runs until cancelled"
            })]);
    }

    [Fact]
    public async Task TaskTool_CancellationToken_FiresWhenExplicitlyCancelled()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "long-running-tool" }, ct);

        Assert.True(augmented.IsTask);
        var taskId = augmented.TaskCreated!.TaskId;

        // Wait for the tool to start executing
        await _toolStarted.Task.WaitAsync(TestConstants.DefaultTimeout, ct);

        // Explicitly cancel the task
        await client.CancelTaskAsync(taskId, ct);

        // Wait for the cancellation to propagate to the tool
        var cancelled = await _toolCancellationFired.Task.WaitAsync(TestConstants.DefaultTimeout, ct);
        Assert.True(cancelled);

        // Verify task status shows cancelled
        var taskResult = await client.GetTaskAsync(taskId, ct);
        Assert.IsType<CancelledTaskResult>(taskResult);
    }

    [Fact]
    public async Task TaskTool_CancellationToken_GetTaskShowsWorkingBeforeCancel()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "long-running-tool" }, ct);

        Assert.True(augmented.IsTask);
        var taskId = augmented.TaskCreated!.TaskId;

        // Wait for the tool to start
        await _toolStarted.Task.WaitAsync(TestConstants.DefaultTimeout, ct);

        // Check status while still running
        var taskResult = await client.GetTaskAsync(taskId, ct);
        Assert.IsType<WorkingTaskResult>(taskResult);

        // Cleanup
        await client.CancelTaskAsync(taskId, ct);
    }
}

/// <summary>
/// Tests for task cancellation with multiple concurrent tasks.
/// </summary>
public class TaskCancellationConcurrencyTests : ClientServerTestBase
{
    private readonly Dictionary<string, TaskCompletionSource<bool>> _toolCancellations = new();
    private readonly Dictionary<string, TaskCompletionSource<bool>> _toolStarts = new();
    private readonly object _lock = new();

    public TaskCancellationConcurrencyTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
#if !NET
        Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "https://github.com/modelcontextprotocol/csharp-sdk/issues/587");
#endif
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.Services.Configure<McpServerOptions>(options =>
        {
            options.TaskStore = new InMemoryMcpTaskStore
            {
                DefaultPollIntervalMs = 50,
            };
        });

        mcpServerBuilder.WithTools([McpServerTool.Create(
            async (string marker, CancellationToken ct) =>
            {
                TaskCompletionSource<bool> startTcs;
                TaskCompletionSource<bool> cancelTcs;

                lock (_lock)
                {
                    if (!_toolStarts.TryGetValue(marker, out startTcs!))
                    {
                        startTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        _toolStarts[marker] = startTcs;
                    }
                    if (!_toolCancellations.TryGetValue(marker, out cancelTcs!))
                    {
                        cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        _toolCancellations[marker] = cancelTcs;
                    }
                }

                startTcs.TrySetResult(true);

                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                    return $"completed-{marker}";
                }
                catch (OperationCanceledException)
                {
                    cancelTcs.TrySetResult(true);
                    throw;
                }
            },
            new McpServerToolCreateOptions
            {
                Name = "trackable-tool",
                Description = "A tool that can be tracked by marker"
            })]);
    }

    private void RegisterMarker(string marker)
    {
        lock (_lock)
        {
            _toolStarts[marker] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _toolCancellations[marker] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private Task WaitForStart(string marker, CancellationToken ct)
    {
        lock (_lock)
        {
            return _toolStarts[marker].Task.WaitAsync(TestConstants.DefaultTimeout, ct);
        }
    }

    private Task<bool> WaitForCancellation(string marker, CancellationToken ct)
    {
        lock (_lock)
        {
            return _toolCancellations[marker].Task.WaitAsync(TestConstants.DefaultTimeout, ct);
        }
    }

    private static IDictionary<string, JsonElement> CreateMarkerArgs(string marker) =>
        new Dictionary<string, JsonElement>
        {
            ["marker"] = JsonDocument.Parse($"\"{marker}\"").RootElement.Clone()
        };

    [Fact]
    public async Task CancelTask_OnlyCancelsTargetTask_NotOtherTasks()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        RegisterMarker("task1");
        RegisterMarker("task2");

        // Start two tasks
        var result1 = await client.CallToolRawAsync(
            new CallToolRequestParams
            {
                Name = "trackable-tool",
                Arguments = CreateMarkerArgs("task1"),
            }, ct);

        var result2 = await client.CallToolRawAsync(
            new CallToolRequestParams
            {
                Name = "trackable-tool",
                Arguments = CreateMarkerArgs("task2"),
            }, ct);

        Assert.True(result1.IsTask);
        Assert.True(result2.IsTask);

        // Wait for both tools to start
        await WaitForStart("task1", ct);
        await WaitForStart("task2", ct);

        // Cancel only task1
        await client.CancelTaskAsync(result1.TaskCreated!.TaskId, ct);

        // task1 should be cancelled
        var task1Cancelled = await WaitForCancellation("task1", ct);
        Assert.True(task1Cancelled);

        // task2 should still be working
        var task2Status = await client.GetTaskAsync(result2.TaskCreated!.TaskId, ct);
        Assert.IsType<WorkingTaskResult>(task2Status);

        // Cleanup
        await client.CancelTaskAsync(result2.TaskCreated!.TaskId, ct);
    }
}

/// <summary>
/// Tests verifying that terminal task states (completed, failed, cancelled) cannot transition.
/// Per spec: "Tasks with a completed, failed, or cancelled status are in a terminal state
/// and MUST NOT transition to any other status"
/// </summary>
public class TerminalTaskStatusTransitionTests : ClientServerTestBase
{
    public TerminalTaskStatusTransitionTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
#if !NET
        Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "https://github.com/modelcontextprotocol/csharp-sdk/issues/587");
#endif
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.Services.Configure<McpServerOptions>(options =>
        {
            options.TaskStore = new InMemoryMcpTaskStore
            {
                DefaultPollIntervalMs = 50,
            };
        });

        mcpServerBuilder.WithTools([
            McpServerTool.Create(
                async (CancellationToken ct) =>
                {
                    await Task.Delay(10, ct);
                    return "quick result";
                },
                new McpServerToolCreateOptions
                {
                    Name = "quick-tool",
                    Description = "A tool that completes quickly"
                }),
            McpServerTool.Create(
                async (CancellationToken ct) =>
                {
                    await Task.Delay(10, ct);
                    throw new InvalidOperationException("Intentional failure");
#pragma warning disable CS0162
                    return "never";
#pragma warning restore CS0162
                },
                new McpServerToolCreateOptions
                {
                    Name = "failing-tool",
                    Description = "A tool that always fails"
                })
        ]);
    }

    [Fact]
    public async Task CompletedTask_CancelIsAcknowledgedIdempotentlyAndStateUnchanged()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "quick-tool" }, ct);

        Assert.True(augmented.IsTask);
        var taskId = augmented.TaskCreated!.TaskId;

        // Wait for completion
        GetTaskResult? taskResult;
        do
        {
            await Task.Delay(50, ct);
            taskResult = await client.GetTaskAsync(taskId, ct);
        }
        while (taskResult is not CompletedTaskResult);

        // SEP-2663: cancel on a terminal task must be acknowledged idempotently.
        var cancelResult = await client.CancelTaskAsync(taskId, ct);
        Assert.NotNull(cancelResult);

        // Verify status is still completed (not flipped to cancelled).
        var verifyResult = await client.GetTaskAsync(taskId, ct);
        Assert.IsType<CompletedTaskResult>(verifyResult);
    }

    [Fact]
    public async Task CompletedWithErrorTask_CancelIsAcknowledgedIdempotently()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "failing-tool" }, ct);

        Assert.True(augmented.IsTask);
        var taskId = augmented.TaskCreated!.TaskId;

        // Wait for completion (tool errors are wrapped as completed with isError=true)
        GetTaskResult? taskResult;
        do
        {
            await Task.Delay(50, ct);
            taskResult = await client.GetTaskAsync(taskId, ct);
        }
        while (taskResult is not CompletedTaskResult);

        // SEP-2663: cancel on a terminal task must be acknowledged idempotently.
        var cancelResult = await client.CancelTaskAsync(taskId, ct);
        Assert.NotNull(cancelResult);

        // Verify status is still completed (not flipped to cancelled).
        var verifyResult = await client.GetTaskAsync(taskId, ct);
        Assert.IsType<CompletedTaskResult>(verifyResult);
    }
}
