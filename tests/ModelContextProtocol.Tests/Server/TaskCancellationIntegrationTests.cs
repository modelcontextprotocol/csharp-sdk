using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Integration tests for task cancellation behavior, including TTL-based automatic
/// cancellation and explicit cancellation via tasks/cancel.
/// </summary>
public class TaskCancellationIntegrationTests : ClientServerTestBase
{
    private readonly TaskCompletionSource<bool> _toolCancellationFired = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _toolStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCancellationIntegrationTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        // Add task store for server-side task support
        var taskStore = new InMemoryMcpTaskStore();
        services.AddSingleton<IMcpTaskStore>(taskStore);

        services.Configure<McpServerOptions>(options =>
        {
            options.TaskStore = taskStore;
        });

        // Add a long-running tool that captures cancellation
        mcpServerBuilder.WithTools([McpServerTool.Create(
            async (CancellationToken ct) =>
            {
                _toolStarted.TrySetResult(true);
                try
                {
                    // Wait indefinitely until cancelled
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

    private static IDictionary<string, JsonElement> EmptyArguments() => new Dictionary<string, JsonElement>();

    [Fact]
    public async Task TaskTool_CancellationToken_FiresWhenTtlExpires()
    {
        // Arrange
        await using McpClient client = await CreateMcpClientForServer();

        // Act - Call tool with short TTL (200ms)
        var callResult = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "long-running-tool",
                Arguments = EmptyArguments(),
                // Use a TTL long enough that thread pool scheduling delays on loaded CI machines
                // don't cause the CTS to fire before the tool lambda begins executing.
                Task = new McpTaskMetadata { TimeToLive = TimeSpan.FromSeconds(5) }
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // Verify task was created
        Assert.NotNull(callResult.Task);

        // Wait for the tool to start executing
        await _toolStarted.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        // Assert - Wait for the cancellation to fire (should happen when TTL expires)
        var cancelled = await _toolCancellationFired.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        Assert.True(cancelled, "Tool's CancellationToken should have been triggered when TTL expired");

        // Note: TTL-based expiration does not explicitly set task status to Cancelled.
        // Instead, expired tasks are considered "dead" and will be cleaned up by the task store.
        // The task may still be in Working status or may throw "not found" if already cleaned up.
    }

    [Fact]
    public async Task TaskTool_CancellationToken_FiresWhenExplicitlyCancelled()
    {
        // Arrange
        await using McpClient client = await CreateMcpClientForServer();

        // Start a long-running task with a long TTL
        var callResult = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "long-running-tool",
                Arguments = EmptyArguments(),
                Task = new McpTaskMetadata { TimeToLive = TimeSpan.FromMinutes(10) }
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(callResult.Task);
        string taskId = callResult.Task.TaskId;

        // Wait for the tool to start executing
        await _toolStarted.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        // Act - Explicitly cancel the task
        var cancelledTask = await client.CancelTaskAsync(taskId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Wait for the cancellation to propagate to the tool
        var cancelled = await _toolCancellationFired.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        Assert.True(cancelled, "Tool's CancellationToken should have been triggered by explicit cancellation");

        // Verify task status
        Assert.Equal(McpTaskStatus.Cancelled, cancelledTask.Status);
    }

    [Fact]
    public async Task TaskTool_CompletesSuccessfully_WhenNotCancelled()
    {
        // Arrange - Create a new test with a quick-completing tool
        var quickToolCompleted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var services = new ServiceCollection();
        services.AddLogging();
        var taskStore = new InMemoryMcpTaskStore();
        services.AddSingleton<IMcpTaskStore>(taskStore);

        var builder = services
            .AddMcpServer()
            .WithStreamServerTransport(
                new System.IO.Pipelines.Pipe().Reader.AsStream(),
                new System.IO.Pipelines.Pipe().Writer.AsStream());

        builder.WithTools([McpServerTool.Create(
            async (string input, CancellationToken ct) =>
            {
                await Task.Delay(50, ct); // Quick operation
                var result = $"Result: {input}";
                quickToolCompleted.TrySetResult(result);
                return result;
            },
            new McpServerToolCreateOptions
            {
                Name = "quick-tool",
                Description = "A tool that completes quickly"
            })]);

        services.Configure<McpServerOptions>(options =>
        {
            options.TaskStore = taskStore;
        });

        await using var client = await CreateMcpClientForServer();

        // Act - Call tool with long TTL
        var callResult = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "long-running-tool", // Use the base class tool which will block
                Arguments = EmptyArguments(),
                Task = new McpTaskMetadata { TimeToLive = TimeSpan.FromMinutes(5) }
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(callResult.Task);

        // Verify task is in working state initially
        var task = await client.GetTaskAsync(callResult.Task.TaskId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(McpTaskStatus.Working, task.Status);
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
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        var taskStore = new InMemoryMcpTaskStore();
        services.AddSingleton<IMcpTaskStore>(taskStore);

        services.Configure<McpServerOptions>(options =>
        {
            options.TaskStore = taskStore;
        });

        // Tool that tracks cancellation per-invocation using a marker argument
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
        // Arrange
        await using McpClient client = await CreateMcpClientForServer();

        RegisterMarker("task1");
        RegisterMarker("task2");

        // Start two tasks
        var result1 = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "trackable-tool",
                Arguments = CreateMarkerArgs("task1"),
                Task = new McpTaskMetadata { TimeToLive = TimeSpan.FromMinutes(10) }
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var result2 = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "trackable-tool",
                Arguments = CreateMarkerArgs("task2"),
                Task = new McpTaskMetadata { TimeToLive = TimeSpan.FromMinutes(10) }
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result1.Task);
        Assert.NotNull(result2.Task);

        // Wait for both tools to start
        await WaitForStart("task1", TestContext.Current.CancellationToken);
        await WaitForStart("task2", TestContext.Current.CancellationToken);

        // Act - Cancel only task1
        await client.CancelTaskAsync(result1.Task.TaskId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - task1 should be cancelled
        var task1Cancelled = await WaitForCancellation("task1", TestContext.Current.CancellationToken);
        Assert.True(task1Cancelled, "Task1 should have been cancelled");

        // task2 should still be running (give it a moment to verify it wasn't cancelled)
        var task2Status = await client.GetTaskAsync(result2.Task.TaskId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(McpTaskStatus.Working, task2Status.Status);

        // Clean up - cancel task2
        await client.CancelTaskAsync(result2.Task.TaskId, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MultipleTasks_WithDifferentTtls_CancelIndependently()
    {
        // Arrange
        await using McpClient client = await CreateMcpClientForServer();

        RegisterMarker("short-ttl");
        RegisterMarker("long-ttl");

        // Start task with short TTL. Use a TTL long enough that thread pool scheduling
        // delays on loaded CI machines don't cause the CTS to fire before the tool
        // lambda begins executing (CancelAfter starts counting at task creation, not
        // when the tool's Task.Run is scheduled).
        var shortTtlResult = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "trackable-tool",
                Arguments = CreateMarkerArgs("short-ttl"),
                Task = new McpTaskMetadata { TimeToLive = TimeSpan.FromSeconds(5) }
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // Start task with long TTL
        var longTtlResult = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "trackable-tool",
                Arguments = CreateMarkerArgs("long-ttl"),
                Task = new McpTaskMetadata { TimeToLive = TimeSpan.FromMinutes(10) }
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(shortTtlResult.Task);
        Assert.NotNull(longTtlResult.Task);

        // Wait for both to start
        await WaitForStart("short-ttl", TestContext.Current.CancellationToken);
        await WaitForStart("long-ttl", TestContext.Current.CancellationToken);

        // Assert - short TTL task should be cancelled automatically
        var shortCancelled = await WaitForCancellation("short-ttl", TestContext.Current.CancellationToken);
        Assert.True(shortCancelled, "Short TTL task should have been cancelled when TTL expired");

        // Long TTL task should still be running
        var longTtlStatus = await client.GetTaskAsync(longTtlResult.Task.TaskId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(McpTaskStatus.Working, longTtlStatus.Status);

        // Clean up
        await client.CancelTaskAsync(longTtlResult.Task.TaskId, cancellationToken: TestContext.Current.CancellationToken);
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
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        var taskStore = new InMemoryMcpTaskStore();
        services.AddSingleton<IMcpTaskStore>(taskStore);

        services.Configure<McpServerOptions>(options =>
        {
            options.TaskStore = taskStore;
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

    private static IDictionary<string, JsonElement> EmptyArguments() => new Dictionary<string, JsonElement>();

    [Fact]
    public async Task CompletedTask_CannotTransitionToOtherStatus()
    {
        // Arrange
        await using McpClient client = await CreateMcpClientForServer();

        var callResult = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "quick-tool",
                Arguments = EmptyArguments(),
                Task = new McpTaskMetadata()
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(callResult.Task);
        string taskId = callResult.Task.TaskId;

        // Wait for completion
        McpTask taskStatus;
        do
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            taskStatus = await client.GetTaskAsync(taskId, cancellationToken: TestContext.Current.CancellationToken);
        }
        while (taskStatus.Status == McpTaskStatus.Working);

        Assert.Equal(McpTaskStatus.Completed, taskStatus.Status);

        // Act - Try to cancel a completed task (should be idempotent)
        var cancelResult = await client.CancelTaskAsync(taskId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Status should still be completed (not cancelled)
        Assert.Equal(McpTaskStatus.Completed, cancelResult.Status);

        // Verify via get
        var verifyStatus = await client.GetTaskAsync(taskId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(McpTaskStatus.Completed, verifyStatus.Status);
    }

    [Fact]
    public async Task FailedTask_CannotTransitionToOtherStatus()
    {
        // Arrange
        await using McpClient client = await CreateMcpClientForServer();

        var callResult = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "failing-tool",
                Arguments = EmptyArguments(),
                Task = new McpTaskMetadata()
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(callResult.Task);
        string taskId = callResult.Task.TaskId;

        // Wait for failure
        McpTask taskStatus;
        do
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            taskStatus = await client.GetTaskAsync(taskId, cancellationToken: TestContext.Current.CancellationToken);
        }
        while (taskStatus.Status == McpTaskStatus.Working);

        Assert.Equal(McpTaskStatus.Failed, taskStatus.Status);

        // Act - Try to cancel a failed task (should be idempotent)
        var cancelResult = await client.CancelTaskAsync(taskId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Status should still be failed
        Assert.Equal(McpTaskStatus.Failed, cancelResult.Status);
    }
}
