using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for <see cref="IMcpTaskExecutor"/>, the extension point that lets a server
/// delegate task execution to an external system instead of running the tool
/// in-process, including the persisted execution intent used for crash recovery.
/// </summary>
public class McpServerTaskExecutorTests : ClientServerTestBase
{
    private const string IntentMarker = "intent-leak-marker";

    private readonly InMemoryMcpTaskStore _taskStore = new() { DefaultPollIntervalMs = 10 };
    private readonly List<string> _events = [];
    private RecordingTaskStore? _recordingStore;
    private readonly TaskCompletionSource<McpTaskExecutionContext> _executorInvoked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _scopeDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _toolStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _toolCancellationFired = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _executorCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? _startException;
    private Exception? _intentException;
    private JsonElement? _executionIntent;
    private bool _runPipelineLocally;
    private int _toolStartCount;

    // The test base class invokes ConfigureServices from its constructor, so the wrapper
    // cannot be assigned in this class's constructor body; resolve it lazily instead.
    private RecordingTaskStore RecordingStore => _recordingStore ??= new RecordingTaskStore(_taskStore, _events);

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
                RecordingStore,
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

    [Fact]
    public async Task CustomExecutor_ExecutionIntent_CreatedBeforeTaskRecordAndPersisted()
    {
        _executionIntent = MakeIntent();

        await using var client = await CreateMcpClientForServer();
        var cancellationToken = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "long-running-tool" },
            cancellationToken);
        Assert.True(augmented.IsTask);

        // Authorization and validation already ran; then create intent, persist task + intent
        // atomically, and start execution — in that order.
        Assert.Equal(["intent", "create", "start"], _events);

        // The context exposes the intent recovered from the store, and the store persisted it
        // with the task record.
        var context = await _executorInvoked.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);
        Assert.True(context.ExecutionIntent.HasValue);
        Assert.Equal(_executionIntent!.Value.GetRawText(), context.ExecutionIntent.Value.GetRawText());

        var stored = await _taskStore.GetTaskAsync(context.TaskId, cancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(_executionIntent.Value.GetRawText(), stored!.ExecutionIntent!.Value.GetRawText());
    }

    [Fact]
    public async Task CustomExecutor_ExecutionIntent_NeverSurfacesInProtocolResponses()
    {
        _executionIntent = MakeIntent();

        await using var client = await CreateMcpClientForServer();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Drive tools/call over the wire and inspect the raw response so the check covers what
        // the server actually sent, not just the deserialized client types.
        JsonRpcRequest callRequest = new()
        {
            Method = RequestMethods.ToolsCall,
            Params = JsonSerializer.SerializeToNode(
                new CallToolRequestParams { Name = "long-running-tool", Meta = CreateTaskCapabilityMeta() },
                McpJsonUtilities.DefaultOptions.GetTypeInfo<CallToolRequestParams>()),
        };
        JsonRpcResponse callResponse = await client.SendRequestAsync(callRequest, cancellationToken);
        var callResult = Assert.IsType<JsonObject>(callResponse.Result);
        Assert.Equal("task", callResult["resultType"]?.GetValue<string>());
        Assert.DoesNotContain(IntentMarker, callResult.ToJsonString());

        var taskId = callResult["taskId"]!.GetValue<string>();

        // Working state: tasks/get must not leak the intent.
        var working = await GetTaskOverWireAsync(client, taskId, cancellationToken);
        Assert.Equal("working", working["status"]?.GetValue<string>());
        Assert.DoesNotContain(IntentMarker, working.ToJsonString());

        // Terminal states: complete the task through the store and poll to completion.
        var result = JsonSerializer.SerializeToElement(
            new CallToolResult { Content = [new TextContentBlock { Text = "external result" }] },
            McpJsonUtilities.DefaultOptions.GetTypeInfo<CallToolResult>());
        await _taskStore.SetCompletedAsync(taskId, result, cancellationToken);

        while (true)
        {
            var terminal = await GetTaskOverWireAsync(client, taskId, cancellationToken);
            if (terminal["status"]?.GetValue<string>() is not "working")
            {
                Assert.DoesNotContain(IntentMarker, terminal.ToJsonString());
                break;
            }

            await Task.Delay(10, cancellationToken);
        }
    }

    [Fact]
    public async Task CustomExecutor_ExecutionIntent_NeverSurfacesInFailedTaskResult()
    {
        _executionIntent = MakeIntent();
        _startException = new InvalidOperationException("external runtime unavailable");

        await using var client = await CreateMcpClientForServer();
        var cancellationToken = TestContext.Current.CancellationToken;

        JsonRpcRequest callRequest = new()
        {
            Method = RequestMethods.ToolsCall,
            Params = JsonSerializer.SerializeToNode(
                new CallToolRequestParams { Name = "long-running-tool", Meta = CreateTaskCapabilityMeta() },
                McpJsonUtilities.DefaultOptions.GetTypeInfo<CallToolRequestParams>()),
        };
        JsonRpcResponse callResponse = await client.SendRequestAsync(callRequest, cancellationToken);
        var callResult = Assert.IsType<JsonObject>(callResponse.Result);
        Assert.Equal("task", callResult["resultType"]?.GetValue<string>());
        Assert.DoesNotContain(IntentMarker, callResult.ToJsonString());

        var taskId = callResult["taskId"]!.GetValue<string>();

        while (true)
        {
            var terminal = await GetTaskOverWireAsync(client, taskId, cancellationToken);
            if (terminal["status"]?.GetValue<string>() is not "working")
            {
                Assert.Equal("failed", terminal["status"]?.GetValue<string>());
                Assert.DoesNotContain(IntentMarker, terminal.ToJsonString());
                break;
            }

            await Task.Delay(10, cancellationToken);
        }
    }

    [Fact]
    public async Task CustomExecutor_CreateIntentThrows_FailsToolsCallWithoutCreatingTask()
    {
        _intentException = new InvalidOperationException("intent construction failed");

        await using var client = await CreateMcpClientForServer();
        var cancellationToken = TestContext.Current.CancellationToken;

        // A failure before the task record exists fails the original tools/call — the caller
        // gets an error result, never a task alternate.
        var augmented = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "long-running-tool" },
            cancellationToken);
        Assert.False(augmented.IsTask);
        Assert.True(augmented.Result!.IsError);

        Assert.Equal(["intent"], _events);
        Assert.False(_executorInvoked.Task.IsCompleted);
        Assert.False(_toolStarted.Task.IsCompleted);
    }

    [Fact]
    public async Task CustomExecutor_StoreRejectingIntent_FailsToolsCallWithoutStartingExecution()
    {
        _executionIntent = MakeIntent();
        RecordingStore.RejectExecutionIntent = true;

        await using var client = await CreateMcpClientForServer();
        var cancellationToken = TestContext.Current.CancellationToken;

        // A store that cannot persist the intent must reject it rather than silently dropping
        // it; the caller gets an error result, no task is created, and execution never starts.
        var augmented = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "long-running-tool" },
            cancellationToken);
        Assert.False(augmented.IsTask);
        Assert.True(augmented.Result!.IsError);

        Assert.Equal(["intent"], _events);
        Assert.False(_executorInvoked.Task.IsCompleted);
        Assert.False(_toolStarted.Task.IsCompleted);
    }

    [Fact]
    public async Task ProcessLocalExecutor_CreateExecutionIntent_ReturnsNull()
    {
        Assert.Null(await ProcessLocalMcpTaskExecutor.Instance.CreateExecutionIntentAsync(
            null!, TestContext.Current.CancellationToken));
    }

    private static JsonElement MakeIntent() => JsonSerializer.SerializeToElement(
        new { queue = IntentMarker, tool = "long-running-tool" },
        McpJsonUtilities.DefaultOptions);

    private static JsonObject CreateTaskCapabilityMeta() => new()
    {
        [MetaKeys.ClientCapabilities] = new JsonObject
        {
            ["extensions"] = new JsonObject
            {
                [TasksProtocol.ExtensionId] = new JsonObject(),
            },
        },
    };

    private static async Task<JsonObject> GetTaskOverWireAsync(
        McpClient client, string taskId, CancellationToken cancellationToken)
    {
        JsonRpcRequest request = new()
        {
            Method = TasksProtocol.MethodTasksGet,
            Params = JsonSerializer.SerializeToNode(
                new GetTaskRequestParams { TaskId = taskId, Meta = CreateTaskCapabilityMeta() },
                McpTasksJsonContext.Default.GetTaskRequestParams),
        };

        JsonRpcResponse response = await client.SendRequestAsync(request, cancellationToken);
        return Assert.IsType<JsonObject>(response.Result);
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
        public ValueTask<JsonElement?> CreateExecutionIntentAsync(
            RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
        {
            test._events.Add("intent");

            if (test._intentException is { } intentException)
            {
                throw intentException;
            }

            return ValueTask.FromResult(test._executionIntent);
        }

        public async ValueTask StartAsync(McpTaskExecutionContext context, CancellationToken cancellationToken)
        {
            test._events.Add("start");
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

    /// <summary>
    /// Wraps a task store to record when <see cref="IMcpTaskStore.CreateTaskAsync"/> runs, so
    /// tests can pin the intent → create → start ordering, and to simulate a store that cannot
    /// persist an execution intent.
    /// </summary>
    private sealed class RecordingTaskStore(IMcpTaskStore inner, List<string> events) : IMcpTaskStore
    {
        public bool RejectExecutionIntent { get; set; }

        public event Action<InputResponseReceivedEventArgs>? InputResponseReceived
        {
            add => inner.InputResponseReceived += value;
            remove => inner.InputResponseReceived -= value;
        }

        public async Task<McpTaskInfo> CreateTaskAsync(
            JsonElement? executionIntent = null,
            CancellationToken cancellationToken = default)
        {
            if (RejectExecutionIntent && executionIntent is not null)
            {
                throw new NotSupportedException("This store does not support persisting an execution intent.");
            }

            var info = await inner.CreateTaskAsync(executionIntent, cancellationToken);
            events.Add("create");
            return info;
        }

        public Task<McpTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
            => inner.GetTaskAsync(taskId, cancellationToken);

        public Task SetCompletedAsync(string taskId, JsonElement result, CancellationToken cancellationToken = default)
            => inner.SetCompletedAsync(taskId, result, cancellationToken);

        public Task SetFailedAsync(string taskId, JsonElement error, CancellationToken cancellationToken = default)
            => inner.SetFailedAsync(taskId, error, cancellationToken);

        public Task<bool> SetCancelledAsync(string taskId, CancellationToken cancellationToken = default)
            => inner.SetCancelledAsync(taskId, cancellationToken);

        public Task ResolveInputRequestsAsync(
            string taskId,
            IDictionary<string, InputResponse> inputResponses,
            CancellationToken cancellationToken = default)
            => inner.ResolveInputRequestsAsync(taskId, inputResponses, cancellationToken);

        public Task SetInputRequestsAsync(
            string taskId,
            IDictionary<string, InputRequest> inputRequests,
            CancellationToken cancellationToken = default)
            => inner.SetInputRequestsAsync(taskId, inputRequests, cancellationToken);
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
        public ValueTask<JsonElement?> CreateExecutionIntentAsync(
            RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
            => ValueTask.FromResult<JsonElement?>(null);

        public ValueTask StartAsync(McpTaskExecutionContext context, CancellationToken cancellationToken)
        {
            test._executorInvoked.TrySetResult(context);
            return context.RunToolPipelineAsync(context.CancellationToken);
        }
    }
}
