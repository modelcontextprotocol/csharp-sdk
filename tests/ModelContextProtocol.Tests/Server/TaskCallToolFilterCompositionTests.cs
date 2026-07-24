using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace ModelContextProtocol.Tests.Server;

#pragma warning disable MCPEXP002 // exercises the experimental alternate-result filter seam
public class TaskCallToolFilterCompositionTests(ITestOutputHelper testOutputHelper) : ClientServerTestBase(testOutputHelper)
{
    private readonly TaskCompletionSource<bool> _continueBackgroundExecution = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<CallToolResult> _backgroundExecutionCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _executionScopeDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _filterInvocationCount;
    private string? _matchedPrimitiveId;

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder
            .WithTools<TaskFilterTools>()
            .WithTasks(new InMemoryMcpTaskStore { DefaultPollIntervalMs = 10 });

        services.AddScoped(_ => new ScopedDependency(_executionScopeDisposed));

        mcpServerBuilder.Services.Configure<McpServerOptions>(options =>
            options.Filters.Request.CallToolFilters.Add(next => async (request, cancellationToken) =>
            {
                Interlocked.Increment(ref _filterInvocationCount);
                _matchedPrimitiveId = request.MatchedPrimitive?.Id;

                if (request.Params?.Name != "scoped-task-filter-tool")
                {
                    return await next(request, cancellationToken);
                }

                try
                {
                    await _continueBackgroundExecution.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);
                    _ = request.Services!.GetRequiredService<ScopedDependency>();
                    var result = await next(request, cancellationToken);
                    _backgroundExecutionCompleted.TrySetResult(result);
                    return result;
                }
                catch (Exception exception)
                {
                    _backgroundExecutionCompleted.TrySetException(exception);
                    throw;
                }
            }));
    }

    [Fact]
    public async Task TaskBackedTool_RunsOrdinaryFilterOnce_WithMatchedPrimitive()
    {
        await using var client = await CreateMcpClientForServer();

        var result = await client.CallToolWithPollingAsync(
            new CallToolRequestParams { Name = "task-filter-tool" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("task filter result", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Equal(1, _filterInvocationCount);
        Assert.Equal("task-filter-tool", _matchedPrimitiveId);
    }

    [Fact]
    public async Task TaskBackedTool_UsesIndependentScopeForBackgroundExecution()
    {
        await using var client = await CreateMcpClientForServer();
        var cancellationToken = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolAsTaskAsync(
            new CallToolRequestParams { Name = "scoped-task-filter-tool" },
            cancellationToken);

        Assert.True(augmented.IsTask);
        _continueBackgroundExecution.TrySetResult(true);

        var result = await _backgroundExecutionCompleted.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);
        Assert.Equal("scoped task result", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.True(await _executionScopeDisposed.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken));
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
        public static string Invoke() => "task filter result";

        [McpServerTool(Name = "scoped-task-filter-tool")]
        public static string InvokeWithScopedDependency(ScopedDependency dependency) => "scoped task result";
    }
}
#pragma warning restore MCPEXP002