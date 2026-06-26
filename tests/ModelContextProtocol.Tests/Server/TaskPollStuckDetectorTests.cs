using ModelContextProtocol.Extensions.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

#pragma warning disable MCPEXP001, MCPEXP002

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Exercises the client-side guard that prevents an unbounded poll loop when a server keeps a
/// task in <see cref="McpTaskStatus.InputRequired"/> without publishing any new input requests
/// after every previously requested input has been resolved.
/// </summary>
public class TaskPollStuckDetectorTests : ClientServerTestBase
{
    private int _pollCount = 0;

    public TaskPollStuckDetectorTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
#if !NET
        Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "https://github.com/modelcontextprotocol/csharp-sdk/issues/587");
#endif
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.Services.Configure<McpServerOptions>(options =>
        {
            options.Capabilities ??= new ServerCapabilities();
            options.Capabilities.Extensions ??= new Dictionary<string, object>();
            options.Capabilities.Extensions[TasksProtocol.ExtensionId] = new JsonObject();
            options.RequestHandlers ??= new List<McpServerRequestHandler>();

            // CallTool always returns a CreateTaskResult with a tiny poll interval so the
            // test exercises the threshold in well under a second.
            options.Handlers.CallToolWithAlternateHandler = (context, cancellationToken) =>
            {
                var taskId = Guid.NewGuid().ToString("N");
                return new ValueTask<ResultOrAlternate<CallToolResult>>(
                    new ResultOrAlternate<CallToolResult>(
                        new CreateTaskResult
                        {
                            TaskId = taskId,
                            Status = McpTaskStatus.InputRequired,
                            CreatedAt = DateTimeOffset.UtcNow,
                            LastUpdatedAt = DateTimeOffset.UtcNow,
                            PollIntervalMs = 5,
                            ResultType = "task",
                        },
                        McpTasksJsonContext.Default.CreateTaskResult));
            };

            // GetTask always reports InputRequired with NO outstanding input requests — the
            // misbehaving-server condition the stuck-detector exists to break out of.
            options.RequestHandlers.Add(new McpServerRequestHandler
            {
                Method = TasksProtocol.MethodTasksGet,
                Handler = (request, cancellationToken) =>
                {
                    var requestParams = JsonSerializer.Deserialize<GetTaskRequestParams>(request.Params, McpTasksJsonContext.Default.Options)
                        ?? throw new McpProtocolException("Missing params for tasks/get", McpErrorCode.InvalidParams);

                    Interlocked.Increment(ref _pollCount);

                    return new ValueTask<JsonNode?>(JsonSerializer.SerializeToNode(new InputRequiredTaskResult
                    {
                        TaskId = requestParams.TaskId,
                        CreatedAt = DateTimeOffset.UtcNow,
                        LastUpdatedAt = DateTimeOffset.UtcNow,
                        PollIntervalMs = 5,
                        InputRequests = new Dictionary<string, InputRequest>(),
                        ResultType = "complete",
                    }, McpTasksJsonContext.Default.Options));
                },
            });

            options.RequestHandlers.Add(new McpServerRequestHandler
            {
                Method = TasksProtocol.MethodTasksCancel,
                Handler = (request, cancellationToken) =>
                    new ValueTask<JsonNode?>(JsonSerializer.SerializeToNode(new CancelTaskResult { ResultType = "complete" }, McpTasksJsonContext.Default.Options)),
            });

            options.RequestHandlers.Add(new McpServerRequestHandler
            {
                Method = TasksProtocol.MethodTasksUpdate,
                Handler = (request, cancellationToken) =>
                    new ValueTask<JsonNode?>(JsonSerializer.SerializeToNode(new UpdateTaskResult { ResultType = "complete" }, McpTasksJsonContext.Default.Options)),
            });
        });
    }

    [Fact]
    public async Task CallToolAsync_TaskStuckInInputRequired_WithoutNewRequests_ThrowsAfterThreshold()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var ex = await Assert.ThrowsAsync<McpException>(async () =>
            await client.CallToolWithPollingAsync(new CallToolRequestParams { Name = "any-tool" }, cancellationToken: ct));

        Assert.Contains(McpTaskStatus.InputRequired.ToString(), ex.Message);
        Assert.Contains("consecutive polls", ex.Message);

        Assert.Equal(60, _pollCount);
    }

    [Fact]
    public async Task CallToolAsync_StuckDetector_HonorsConfiguredThreshold()
    {
        // Verifies CallToolWithPollingAsync plumbs the explicit threshold into PollTaskToCompletionAsync:
        // a smaller configured threshold is surfaced verbatim in the McpException message.
        const int CustomThreshold = 3;

        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var ex = await Assert.ThrowsAsync<McpException>(async () =>
            await client.CallToolWithPollingAsync(new CallToolRequestParams { Name = "any-tool" }, maxConsecutiveStuckPolls: CustomThreshold, cancellationToken: ct));

        // The message embeds the configured threshold, which is the strongest signal that the
        // option value (not the 60-default constant) is what governed the loop.
        Assert.Contains($"{CustomThreshold} consecutive polls", ex.Message);
        Assert.Equal(CustomThreshold, _pollCount);
    }

}
