using ModelContextProtocol.Extensions.Tasks;
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
        mcpServerBuilder
            .WithTasks(new InMemoryMcpTaskStore
            {
                DefaultPollIntervalMs = 50,
                DefaultTimeToLive = TimeSpan.FromSeconds(5),
            })
            .WithTools([McpServerTool.Create(
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

        var augmented = await client.CallToolAsTaskAsync(
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

        var augmented = await client.CallToolAsTaskAsync(
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
/// Tests for task-store runner cleanup during server disposal.
/// </summary>
public class TaskRunnerLifecycleTests : ClientServerTestBase
{
    private readonly TaskCompletionSource<bool> _toolStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _toolCancellationFired = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _releaseCancellationCleanup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _forceToolExit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _toolExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _runnerRegistrationBlocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _releaseRunnerRegistration = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly BlockingCancellationTaskStore _taskStore = new();
    private bool _delayRunnerRegistration;

    public TaskRunnerLifecycleTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
#if !NET
        Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "https://github.com/modelcontextprotocol/csharp-sdk/issues/587");
#endif
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
#pragma warning disable MCPEXP002
        services.Configure<McpServerOptions>(options =>
            options.Filters.Request.CallToolWithAlternateFilters.Add(next => async (request, cancellationToken) =>
            {
                if (_delayRunnerRegistration && request.Params?.Name == "lifecycle-tool")
                {
                    _runnerRegistrationBlocked.TrySetResult(true);
                    await _releaseRunnerRegistration.Task;
                }

                return await next(request, cancellationToken);
            }));
#pragma warning restore MCPEXP002

        mcpServerBuilder
            .WithTasks(_taskStore)
            .WithTools([McpServerTool.Create(
            async (CancellationToken ct) =>
            {
                _toolStarted.TrySetResult(true);
                try
                {
                    var cancellationTask = Task.Delay(Timeout.Infinite, ct);
                    var completedTask = await Task.WhenAny(cancellationTask, _forceToolExit.Task);
                    await completedTask;
                    return "forced test cleanup";
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    _toolCancellationFired.TrySetResult(true);
                    await _releaseCancellationCleanup.Task;
                    throw;
                }
                finally
                {
                    _toolExited.TrySetResult(true);
                }
            },
            new McpServerToolCreateOptions
            {
                Name = "lifecycle-tool",
                Description = "A tool used to verify task runner lifecycle"
            })]);
    }

    [Fact]
    public async Task DisposeAsync_CancelsAndWaitsForTaskStoreRunner()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "lifecycle-tool" }, ct);
        Assert.True(augmented.IsTask);

        await _toolStarted.Task.WaitAsync(TestConstants.DefaultTimeout, ct);
        Task disposeTask = Server.DisposeAsync().AsTask();

        try
        {
            Task firstCompleted = await Task.WhenAny(_toolCancellationFired.Task, disposeTask)
                .WaitAsync(TestConstants.DefaultTimeout, ct);

            Assert.Same(_toolCancellationFired.Task, firstCompleted);
            Assert.False(disposeTask.IsCompleted, "DisposeAsync should wait for the runner's cancellation cleanup.");

            _releaseCancellationCleanup.TrySetResult(true);
            await disposeTask.WaitAsync(TestConstants.DefaultTimeout, ct);
        }
        finally
        {
            _releaseCancellationCleanup.TrySetResult(true);
            _forceToolExit.TrySetResult(true);
            await _toolExited.Task.WaitAsync(TestConstants.DefaultTimeout, ct);
        }
    }

    [Fact]
    public async Task DisposeAsync_CancelsAndWaitsForRunnerRegisteredDuringDisposal()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;
        _delayRunnerRegistration = true;
        _taskStore.PauseCancellationRecording();

        var callTask = client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "lifecycle-tool" }, ct).AsTask();
        _ = callTask.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        await _runnerRegistrationBlocked.Task.WaitAsync(TestConstants.DefaultTimeout, ct);

        var serverLifetime = Assert.IsAssignableFrom<IMcpServerLifetimeFeature>(Server);
        var serverCancellationFired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = serverLifetime.BackgroundTaskCancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), serverCancellationFired);

        Task disposeTask = Server.DisposeAsync().AsTask();

        try
        {
            await serverCancellationFired.Task.WaitAsync(TestConstants.DefaultTimeout, ct);
            _releaseRunnerRegistration.TrySetResult(true);

            await _taskStore.CancellationRecordingStarted.WaitAsync(TestConstants.DefaultTimeout, ct);
            Assert.False(disposeTask.IsCompleted, "DisposeAsync should wait for a runner registered during disposal.");

            _taskStore.ReleaseCancellationRecording();
            await disposeTask.WaitAsync(TestConstants.DefaultTimeout, ct);
        }
        finally
        {
            _releaseRunnerRegistration.TrySetResult(true);
            _releaseCancellationCleanup.TrySetResult(true);
            _taskStore.ReleaseCancellationRecording();
            _forceToolExit.TrySetResult(true);

            if (_toolStarted.Task.IsCompleted)
            {
                await _toolExited.Task.WaitAsync(TestConstants.DefaultTimeout, ct);
            }
        }
    }

    private sealed class BlockingCancellationTaskStore : InMemoryMcpTaskStore, IMcpTaskStore
    {
        private readonly TaskCompletionSource<bool> _cancellationRecordingStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseCancellationRecording = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _pauseCancellationRecording;

        public Task CancellationRecordingStarted => _cancellationRecordingStarted.Task;

        public void PauseCancellationRecording() => _pauseCancellationRecording = true;

        public void ReleaseCancellationRecording() => _releaseCancellationRecording.TrySetResult(true);

        async Task<bool> IMcpTaskStore.SetCancelledAsync(string taskId, CancellationToken cancellationToken)
        {
            if (_pauseCancellationRecording)
            {
                _cancellationRecordingStarted.TrySetResult(true);
                await _releaseCancellationRecording.Task;
            }

            return await base.SetCancelledAsync(taskId, cancellationToken);
        }
    }
}

public class McpServerLifetimeFeatureTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    [Fact]
    public async Task DisposeAsync_DoesNotCancelOrWaitForStatelessBackgroundTask()
    {
        await using var transport = new StreamableHttpServerTransport { Stateless = true };
        await using var statelessServer = McpServer.Create(
            transport,
            new McpServerOptions
            {
                ServerInfo = new Implementation { Name = "test-server", Version = "1.0" },
            },
            LoggerFactory);
        var serverLifetime = Assert.IsAssignableFrom<IMcpServerLifetimeFeature>(statelessServer);
        var releaseBackgroundTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task backgroundTask = releaseBackgroundTask.Task;

        serverLifetime.RegisterBackgroundTask(backgroundTask);

        try
        {
            await statelessServer.DisposeAsync().AsTask()
                .WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

            Assert.False(serverLifetime.BackgroundTaskCancellationToken.CanBeCanceled);
            Assert.False(backgroundTask.IsCompleted,
                "A stateless per-request server should not own background work that outlives the request.");
        }
        finally
        {
            releaseBackgroundTask.TrySetResult(true);
            await backgroundTask;
        }
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
        mcpServerBuilder
            .WithTasks(new InMemoryMcpTaskStore
            {
                DefaultPollIntervalMs = 50,
            })
            .WithTools([McpServerTool.Create(
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
        var result1 = await client.CallToolAsTaskAsync(
            new CallToolRequestParams
            {
                Name = "trackable-tool",
                Arguments = CreateMarkerArgs("task1"),
            }, ct);

        var result2 = await client.CallToolAsTaskAsync(
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
        mcpServerBuilder
            .WithTasks(new InMemoryMcpTaskStore
            {
                DefaultPollIntervalMs = 50,
            })
            .WithTools([
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

        var augmented = await client.CallToolAsTaskAsync(
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

        var augmented = await client.CallToolAsTaskAsync(
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
