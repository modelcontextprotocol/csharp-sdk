using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests.Server;

public class TaskCallToolFilterCompositionTests(ITestOutputHelper testOutputHelper) : ClientServerTestBase(testOutputHelper)
{
    private readonly TaskCompletionSource<bool> _continueBackgroundExecution = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<CallToolResult> _executionCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _executionScopeDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _filterInvocationCount;
    private string? _matchedPrimitiveId;

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.AddScoped(_ => new ScopedDependency(_executionScopeDisposed));

        mcpServerBuilder
            .WithTools<TaskFilterTools>()
            .WithTasks(new InMemoryMcpTaskStore { DefaultPollIntervalMs = 10 });

        mcpServerBuilder.Services.Configure<McpServerOptions>(options =>
            options.Filters.Request.CallToolFilters.Add(next => async (request, cancellationToken) =>
            {
                Interlocked.Increment(ref _filterInvocationCount);
                _matchedPrimitiveId = request.MatchedPrimitive?.Id;

                try
                {
                    await _continueBackgroundExecution.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);
                    _ = request.Services!.GetRequiredService<ScopedDependency>();
                    var result = await next(request, cancellationToken);
                    _executionCompleted.TrySetResult(result);
                    return result;
                }
                catch (Exception exception)
                {
                    _executionCompleted.TrySetException(exception);
                    throw;
                }
            }));
    }

    [Fact]
    public async Task TaskBackedTool_RunsOrdinaryFilterOnce_InIndependentScope()
    {
        await using var client = await CreateMcpClientForServer();
        var cancellationToken = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "task-filter-tool" },
            cancellationToken);

        Assert.True(augmented.IsTask);
        _continueBackgroundExecution.TrySetResult(true);

        var result = await _executionCompleted.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);
        Assert.Equal("task filter result", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Equal(1, _filterInvocationCount);
        Assert.Equal("task-filter-tool", _matchedPrimitiveId);
        Assert.True(await _executionScopeDisposed.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken));

        var task = await client.GetTaskAsync(augmented.TaskCreated!.TaskId, cancellationToken);
        Assert.IsType<CompletedTaskResult>(task);
    }

    private sealed class ScopedDependency(TaskCompletionSource<bool> disposed) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            disposed.TrySetResult(true);
            return default;
        }
    }

    [McpServerToolType]
    private sealed class TaskFilterTools
    {
        [McpServerTool(Name = "task-filter-tool")]
        public static string Invoke(ScopedDependency dependency) => "task filter result";
    }
}
