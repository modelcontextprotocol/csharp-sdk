using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for <see cref="IMcpTaskExecutor"/>, the extension point that lets a server
/// delegate task execution to an external system instead of running the tool
/// in-process.
/// </summary>
public class McpServerTaskExecutorTests : ClientServerTestBase
{
    private readonly InMemoryMcpTaskStore _taskStore = new() { DefaultPollIntervalMs = 10 };
    private readonly TaskCompletionSource<McpTaskExecutionContext> _executorInvoked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _scopeDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _toolStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _toolCancellationFired = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _executorCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? _startException;
    private bool _runPipelineLocally;
    private int _toolStartCount;

    public McpServerTaskExecutorTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
#if !NET
        Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "https://github.com/modelcontextprotocol/csharp-sdk/issues/587");
#endif
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.AddScoped(_ => new ScopedDependency(_scopeDisposed));

        mcpServerBuilder
            .WithTasks(
                _taskStore,
                options =>
                {
                    options.TaskExecutor = new CallbackTaskExecutor(this);
                })
            .WithTools([McpServerTool.Create(
                async (CancellationToken ct) =>
                {
                    Interlocked.Increment(ref _toolStartCount);
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
                new McpServerToolCreateOptions { Name = "long-running-tool" }),
            McpServerTool.Create(
                () => "local result",
                new McpServerToolCreateOptions { Name = "local-tool" })]);
    }

    [Fact]
    public async Task CustomExecutor_ReceivesTaskAndRequestBoundToExecutionScope()
    {
        await using var client = await CreateMcpClientForServer();
        var cancellationToken = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "long-running-tool" },
            cancellationToken);

        Assert.True(augmented.IsTask);
        var context = await _executorInvoked.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);
        Assert.Equal(augmented.TaskCreated!.TaskId, context.TaskId);
        Assert.Equal(McpTaskStatus.Working, context.TaskInfo.Status);
        Assert.Equal("long-running-tool", context.Request.MatchedPrimitive?.Id);
        Assert.NotNull(context.Request.Services);
        Assert.Same(
            context.Request.Services!.GetRequiredService<ScopedDependency>(),
            context.Request.Services.GetRequiredService<ScopedDependency>());

        // The tool body must not run until the executor starts the pipeline.
        Assert.False(_toolStarted.Task.IsCompleted);
    }

    [Fact]
    public async Task CustomExecutor_NotRunningPipeline_ToolBodyNeverRunsInProcess()
    {
        await using var client = await CreateMcpClientForServer();
        var cancellationToken = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "long-running-tool" },
            cancellationToken);

        Assert.True(augmented.IsTask);
        var context = await _executorInvoked.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);

        // Simulate the external runtime completing the task directly through the store.
        var result = JsonSerializer.SerializeToElement(
            new CallToolResult { Content = [new TextContentBlock { Text = "external result" }] },
            McpJsonUtilities.DefaultOptions.GetTypeInfo<CallToolResult>());
        await _taskStore.SetCompletedAsync(context.TaskId, result, cancellationToken);

        var task = await PollUntilTerminalAsync(client, augmented.TaskCreated!.TaskId, cancellationToken);
        Assert.IsType<CompletedTaskResult>(task);
        Assert.False(_toolStarted.Task.IsCompleted);
    }

    [Fact]
    public async Task CustomExecutor_RunsPipelineLocally_ResultRecordedInStore()
    {
        _runPipelineLocally = true;

        await using var client = await CreateMcpClientForServer();
        var cancellationToken = TestContext.Current.CancellationToken;

        var result = await client.CallToolWithPollingAsync(
            new CallToolRequestParams { Name = "local-tool" },
            cancellationToken: cancellationToken);

        Assert.Equal("local result", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task CustomExecutor_RunsPipelineLocally_ScopeDisposedAfterCompletion()
    {
        _runPipelineLocally = true;

        await using var client = await CreateMcpClientForServer();
        var cancellationToken = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "local-tool" },
            cancellationToken);
        Assert.True(augmented.IsTask);

        var task = await PollUntilTerminalAsync(client, augmented.TaskCreated!.TaskId, cancellationToken);
        Assert.IsType<CompletedTaskResult>(task);
        Assert.True(await _scopeDisposed.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken));
    }

    [Fact]
    public async Task CustomExecutor_ThrowingFromStartAsync_MarksTaskFailed()
    {
        _startException = new InvalidOperationException("external runtime unavailable");

        await using var client = await CreateMcpClientForServer();
        var cancellationToken = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "long-running-tool" },
            cancellationToken);

        Assert.True(augmented.IsTask);

        var task = await PollUntilTerminalAsync(client, augmented.TaskCreated!.TaskId, cancellationToken);
        var failed = Assert.IsType<FailedTaskResult>(task);
        Assert.Contains("external runtime unavailable", failed.Error.GetRawText());
        Assert.False(_toolStarted.Task.IsCompleted);
    }

    [Fact]
    public async Task CustomExecutor_TasksCancel_FiresExecutorCancellationToken()
    {
        await using var client = await CreateMcpClientForServer();
        var cancellationToken = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "long-running-tool" },
            cancellationToken);
        Assert.True(augmented.IsTask);
        await _executorInvoked.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);

        await client.CancelTaskAsync(augmented.TaskCreated!.TaskId, cancellationToken);

        Assert.True(await _executorCancelled.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken));
    }

    [Fact]
    public async Task CustomExecutor_DisposesContext_ReleasesScopeWithoutRunningTool()
    {
        await using var client = await CreateMcpClientForServer();
        var cancellationToken = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "long-running-tool" },
            cancellationToken);
        Assert.True(augmented.IsTask);
        var context = await _executorInvoked.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);

        await context.DisposeAsync();

        await _scopeDisposed.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);
        Assert.False(_toolStarted.Task.IsCompleted);

        // DisposeAsync is idempotent.
        await context.DisposeAsync();
    }

    [Fact]
    public async Task CustomExecutor_RunsPipelineAfterDispose_ThrowsObjectDisposed()
    {
        await using var client = await CreateMcpClientForServer();
        var cancellationToken = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "long-running-tool" },
            cancellationToken);
        Assert.True(augmented.IsTask);
        var context = await _executorInvoked.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);

        await context.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => context.RunToolPipelineAsync(cancellationToken).AsTask());
    }

    [Fact]
    public async Task CustomExecutor_StartsPipelineLater_TokenCancelsPipeline()
    {
        await using var client = await CreateMcpClientForServer();
        var cancellationToken = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "long-running-tool" },
            cancellationToken);
        Assert.True(augmented.IsTask);
        var context = await _executorInvoked.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);

        // The executor hands the task to an external runtime, which later runs the pipeline
        // locally. Cancelling via tasks/cancel must fire the context token and cancel the
        // pipeline.
        _ = Task.Run(() => context.RunToolPipelineAsync(context.CancellationToken).AsTask(), CancellationToken.None);
        await _toolStarted.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);

        await client.CancelTaskAsync(augmented.TaskCreated!.TaskId, cancellationToken);

        Assert.True(await _toolCancellationFired.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken));

        var task = await PollUntilTerminalAsync(client, augmented.TaskCreated!.TaskId, cancellationToken);
        Assert.IsType<CancelledTaskResult>(task);
    }

    [Fact]
    public async Task CustomExecutor_ConcurrentPipelineStarts_RunToolOnlyOnce()
    {
        await using var client = await CreateMcpClientForServer();
        var cancellationToken = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "long-running-tool" },
            cancellationToken);
        Assert.True(augmented.IsTask);
        var context = await _executorInvoked.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);

        // Two concurrent starts of the pipeline: exactly one may win; the loser must observe
        // the context as disposed rather than starting a second execution of the tool.
        var first = Task.Run(() => context.RunToolPipelineAsync(context.CancellationToken).AsTask(), CancellationToken.None);
        var second = Task.Run(() => context.RunToolPipelineAsync(context.CancellationToken).AsTask(), CancellationToken.None);

        await _toolStarted.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);
        Assert.Equal(1, _toolStartCount);

        var loser = await Task.WhenAny(first, second);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => loser);

        // The winning execution is still the one recorded in the store.
        await client.CancelTaskAsync(augmented.TaskCreated!.TaskId, cancellationToken);
        var task = await PollUntilTerminalAsync(client, augmented.TaskCreated!.TaskId, cancellationToken);
        Assert.IsType<CancelledTaskResult>(task);
        Assert.Equal(1, _toolStartCount);
    }

    private static async Task<GetTaskResult> PollUntilTerminalAsync(
        McpClient client, string taskId, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var task = await client.GetTaskAsync(taskId, cancellationToken);
            if (task is not WorkingTaskResult)
            {
                return task;
            }

            await Task.Delay(10, cancellationToken);
        }
    }

    private sealed class CallbackTaskExecutor(McpServerTaskExecutorTests test) : IMcpTaskExecutor
    {
        public async ValueTask StartAsync(McpTaskExecutionContext context, CancellationToken cancellationToken)
        {
            test._executorInvoked.TrySetResult(context);
            context.CancellationToken.Register(() => test._executorCancelled.TrySetResult(true));

            // Materialize a scoped dependency so the tests can observe scope disposal, the way
            // an external executor reads scope-bound services before handing work off.
            _ = context.Request.Services!.GetRequiredService<ScopedDependency>();

            if (test._startException is { } exception)
            {
                throw exception;
            }

            if (test._runPipelineLocally)
            {
                await context.RunToolPipelineAsync(context.CancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class ScopedDependency(TaskCompletionSource<bool> disposed) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            disposed.TrySetResult(true);
            return default;
        }
    }
}

public class McpServerTaskExecutorDiResolutionTests : ClientServerTestBase
{
    private readonly TaskCompletionSource<McpTaskExecutionContext> _executorInvoked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _executorInstances;

    public McpServerTaskExecutorDiResolutionTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
#if !NET
        Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "https://github.com/modelcontextprotocol/csharp-sdk/issues/587");
#endif
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.AddScoped<IMcpTaskExecutor>(_ =>
        {
            Interlocked.Increment(ref _executorInstances);
            return new DiTaskExecutor(this);
        });

        mcpServerBuilder
            .WithTasks(new InMemoryMcpTaskStore { DefaultPollIntervalMs = 10 })
            .WithTools([McpServerTool.Create(
                () => "local result",
                new McpServerToolCreateOptions { Name = "local-tool" })]);
    }

    [Fact]
    public async Task ScopedExecutor_RegisteredInDi_IsResolvedPerTaskAndRunsPipeline()
    {
        await using var client = await CreateMcpClientForServer();
        var cancellationToken = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "local-tool" },
            cancellationToken);
        Assert.True(augmented.IsTask);

        await _executorInvoked.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);
        var task = await PollUntilTerminalAsync(client, augmented.TaskCreated!.TaskId, cancellationToken);
        Assert.IsType<CompletedTaskResult>(task);
        Assert.Equal(1, _executorInstances);

        // A second task resolves a fresh scoped executor instance.
        var second = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "local-tool" },
            cancellationToken);
        Assert.True(second.IsTask);
        var secondTask = await PollUntilTerminalAsync(client, second.TaskCreated!.TaskId, cancellationToken);
        Assert.IsType<CompletedTaskResult>(secondTask);
        Assert.Equal(2, _executorInstances);
    }

    private static async Task<GetTaskResult> PollUntilTerminalAsync(
        McpClient client, string taskId, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var task = await client.GetTaskAsync(taskId, cancellationToken);
            if (task is not WorkingTaskResult)
            {
                return task;
            }

            await Task.Delay(10, cancellationToken);
        }
    }

    private sealed class DiTaskExecutor(McpServerTaskExecutorDiResolutionTests test) : IMcpTaskExecutor
    {
        public ValueTask StartAsync(McpTaskExecutionContext context, CancellationToken cancellationToken)
        {
            test._executorInvoked.TrySetResult(context);
            return context.RunToolPipelineAsync(context.CancellationToken);
        }
    }
}
