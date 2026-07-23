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
    private int _alternateFilterFactoryInvocationCount;
    private int _filterInvocationCount;
    private string? _matchedPrimitiveId;
    private string? _alternateMatchedPrimitiveId;
    private string? _throwingAlternateMatchedPrimitiveId;
    private RequestContext<CallToolRequestParams>? _alternateRequestContext;
    private RequestContext<CallToolRequestParams>? _ordinaryRequestContext;
    private IServiceProvider? _alternateServicesBeforeNext;
    private IServiceProvider? _alternateServicesAfterNext;

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.AddScoped(_ => new ScopedDependency(_executionScopeDisposed));

        mcpServerBuilder
            .WithTools<TaskFilterTools>()
            .WithTasks(new InMemoryMcpTaskStore { DefaultPollIntervalMs = 10 });

        mcpServerBuilder.Services.Configure<McpServerOptions>(options =>
        {
            options.Filters.Request.CallToolFilters.Add(next => async (request, cancellationToken) =>
            {
                if (request.Params?.Name != "task-filter-tool")
                {
                    return await next(request, cancellationToken);
                }

                Interlocked.Increment(ref _filterInvocationCount);
                _matchedPrimitiveId = request.MatchedPrimitive?.Id;
                _ordinaryRequestContext = request;

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
            });

#pragma warning disable MCPEXP002 // exercises the experimental CallToolWithAlternateFilters seam
            options.Filters.Request.CallToolWithAlternateFilters.Add(next =>
            {
                Interlocked.Increment(ref _alternateFilterFactoryInvocationCount);
                return async (request, cancellationToken) =>
                {
                    if (request.Params?.Name == "alternate-filter-exception-tool")
                    {
                        _throwingAlternateMatchedPrimitiveId = request.MatchedPrimitive?.Id;
                        throw new InvalidOperationException("Alternate filter failure.");
                    }

                    if (request.Params?.Name == "alternate-short-circuit-tool")
                    {
                        return new CallToolResult
                        {
                            Content = [new TextContentBlock { Text = "short-circuited" }],
                        };
                    }

                    if (request.Params?.Name == "replace-jsonrpc-request-tool")
                    {
                        request.JsonRpcRequest = new JsonRpcRequest
                        {
                            Id = request.JsonRpcRequest.Id,
                            Method = request.JsonRpcRequest.Method,
                        };
                    }

                    if (request.Params?.Name == "alternate-transform-result-tool")
                    {
                        _ = await next(request, cancellationToken);
                        return new CallToolResult
                        {
                            IsError = true,
                            Content = [new TextContentBlock { Text = "transformed" }],
                        };
                    }

                    _alternateMatchedPrimitiveId = request.MatchedPrimitive?.Id;
                    _alternateRequestContext = request;
                    _alternateServicesBeforeNext = request.Services;
                    var result = await next(request, cancellationToken);
                    _alternateServicesAfterNext = request.Services;
                    return result;
                };
            });
#pragma warning restore MCPEXP002
        });
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
        Assert.Equal("task-filter-tool", _alternateMatchedPrimitiveId);
        Assert.NotSame(_alternateRequestContext, _ordinaryRequestContext);
        Assert.Same(_alternateServicesBeforeNext, _alternateServicesAfterNext);
        Assert.True(await _executionScopeDisposed.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken));

        var task = await client.GetTaskAsync(augmented.TaskCreated!.TaskId, cancellationToken);
        Assert.IsType<CompletedTaskResult>(task);
    }

    [Fact]
    public async Task AlternateFilterException_IsConvertedToCallToolError()
    {
        await using var client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "alternate-filter-exception-tool",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(
            "An error occurred invoking 'alternate-filter-exception-tool'.",
            Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Equal("alternate-filter-exception-tool", _throwingAlternateMatchedPrimitiveId);
    }

    [Fact]
    public async Task AlternateFilterShortCircuit_LogsCompletionOnce()
    {
        await using var client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "alternate-short-circuit-tool",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("short-circuited", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Single(
            MockLoggerProvider.LogMessages,
            message => message.Message == "\"alternate-short-circuit-tool\" completed. IsError = False.");
    }

    [Fact]
    public async Task AlternateFilterFactory_IsCreatedOnce()
    {
        await using var client = await CreateMcpClientForServer();

        await client.CallToolAsync(
            "alternate-short-circuit-tool",
            cancellationToken: TestContext.Current.CancellationToken);
        await client.CallToolAsync(
            "alternate-short-circuit-tool",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, _alternateFilterFactoryInvocationCount);
    }

    [Fact]
    public async Task AlternateFilter_CanReplaceJsonRpcRequest()
    {
        await using var client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "replace-jsonrpc-request-tool",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("replacement succeeded", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task AlternateFilterTransformedResult_LogsFinalResult()
    {
        await using var client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync(
            "alternate-transform-result-tool",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal("transformed", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        var completionLog = Assert.Single(
            MockLoggerProvider.LogMessages,
            message => message.Message.StartsWith("\"alternate-transform-result-tool\" completed.", StringComparison.Ordinal));
        Assert.Equal("\"alternate-transform-result-tool\" completed. IsError = True.", completionLog.Message);
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

        [McpServerTool(Name = "alternate-filter-exception-tool")]
        public static string ThrowingAlternateFilterTarget() => "unreachable";

        [McpServerTool(Name = "alternate-short-circuit-tool")]
        public static string ShortCircuitedAlternateFilterTarget() => "unreachable";

        [McpServerTool(Name = "replace-jsonrpc-request-tool")]
        public static string ReplaceJsonRpcRequestTarget() => "replacement succeeded";

        [McpServerTool(Name = "alternate-transform-result-tool")]
        public static string AlternateTransformResultTarget() => "original";
    }
}
