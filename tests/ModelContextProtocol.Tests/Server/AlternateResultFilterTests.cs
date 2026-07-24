using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Server;

#pragma warning disable MCPEXP002 // exercises the experimental alternate-result filter seam
public class AlternateResultFilterTests(ITestOutputHelper testOutputHelper) : ClientServerTestBase(testOutputHelper)
{
    private readonly List<string> _invocations = [];

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder) =>
        mcpServerBuilder.Services.Configure<McpServerOptions>(options =>
        {
            options.Handlers.GetPromptHandler = (request, cancellationToken) =>
            {
                _invocations.Add("handler");
                return new ValueTask<GetPromptResult>(new GetPromptResult
                {
                    Description = "normal result",
                });
            };

            options.AddAlternateResultFilter<GetPromptRequestParams, GetPromptResult>(
                RequestMethods.PromptsGet,
                next => async (request, cancellationToken) =>
                {
                    _invocations.Add("outer-before");
                    var result = await next(request, cancellationToken);
                    _invocations.Add("outer-after");
                    return result;
                });

            options.AddAlternateResultFilter<GetPromptRequestParams, GetPromptResult>(
                RequestMethods.PromptsGet,
                next => async (request, cancellationToken) =>
                {
                    _invocations.Add("filter");
                    if (request.Params?.Name == "as-task")
                    {
                        return ResultOrAlternate<GetPromptResult>.FromAlternate(
                            new CreateTaskResult
                            {
                                TaskId = "prompt-task",
                                Status = McpTaskStatus.Working,
                                CreatedAt = DateTimeOffset.UnixEpoch,
                                LastUpdatedAt = DateTimeOffset.UnixEpoch,
                            },
                            McpTasksJsonContext.Default.CreateTaskResult);
                    }

                    return await next(request, cancellationToken);
                });

            options.AddAlternateResultFilter<ListPromptsRequestParams, ListPromptsResult>(
                RequestMethods.PromptsList,
                next => (request, cancellationToken) =>
                    new ValueTask<ResultOrAlternate<ListPromptsResult>>(new ListPromptsResult()));
        });

    [Fact]
    public async Task AlternateResultFilter_OnPromptHandler_CanReturnAlternateResult()
    {
        await using var client = await CreateMcpClientForServer();
        var serializerOptions = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        serializerOptions.TypeInfoResolverChain.Add(McpTasksJsonContext.Default);

        var result = await client.SendRequestAsync<GetPromptRequestParams, CreateTaskResult>(
            RequestMethods.PromptsGet,
            new GetPromptRequestParams { Name = "as-task" },
            serializerOptions: serializerOptions,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("prompt-task", result.TaskId);
        Assert.Equal(["outer-before", "filter", "outer-after"], _invocations);
    }

    [Fact]
    public async Task AlternateResultFilter_OnPromptHandler_CanInvokeNormalPipeline()
    {
        await using var client = await CreateMcpClientForServer();

        var result = await client.GetPromptAsync(
            "normal",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("normal result", result.Description);
        Assert.Equal(["outer-before", "filter", "handler", "outer-after"], _invocations);
    }

    [Fact]
    public async Task AlternateResultFilter_NormalizesCacheableImmediateResult()
    {
        await using var client = await CreateMcpClientForServer();

        var result = await client.SendRequestAsync<ListPromptsRequestParams, ListPromptsResult>(
            RequestMethods.PromptsList,
            new ListPromptsRequestParams(),
            serializerOptions: McpJsonUtilities.DefaultOptions,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.Zero, result.TimeToLive);
        Assert.Equal(CacheScope.Private, result.CacheScope);
        Assert.Equal("complete", result.ResultType);
    }
}
#pragma warning restore MCPEXP002