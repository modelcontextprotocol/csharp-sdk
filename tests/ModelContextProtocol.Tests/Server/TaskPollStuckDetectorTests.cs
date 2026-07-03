using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Runtime.InteropServices;

#pragma warning disable MCPEXP001

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

            // CallTool always returns a CreateTaskResult with a tiny poll interval so the
            // test exercises the threshold in well under a second.
            options.Handlers.CallToolWithTaskHandler = (context, cancellationToken) =>
            {
                var taskId = Guid.NewGuid().ToString("N");
                return new ValueTask<ResultOrCreatedTask<CallToolResult>>(new CreateTaskResult
                {
                    TaskId = taskId,
                    Status = McpTaskStatus.InputRequired,
                    CreatedAt = DateTimeOffset.UtcNow,
                    LastUpdatedAt = DateTimeOffset.UtcNow,
                    PollIntervalMs = 5,
                    ResultType = "task",
                });
            };

            // GetTask always reports InputRequired with NO outstanding input requests — the
            // misbehaving-server condition the stuck-detector exists to break out of.
            options.Handlers.GetTaskHandler = (context, cancellationToken) =>
            {
                Interlocked.Increment(ref _pollCount);

                return new ValueTask<GetTaskResult>(new InputRequiredTaskResult
                {
                    TaskId = context.Params!.TaskId,
                    CreatedAt = DateTimeOffset.UtcNow,
                    LastUpdatedAt = DateTimeOffset.UtcNow,
                    PollIntervalMs = 5,
                    InputRequests = new Dictionary<string, InputRequest>(),
                    ResultType = "complete",
                });
            };

            // CancelTask must succeed since the client issues a best-effort cancel when it
            // gives up; otherwise the cancel failure would mask the real exception.
            options.Handlers.CancelTaskHandler = (context, cancellationToken) =>
                new ValueTask<CancelTaskResult>(new CancelTaskResult { ResultType = "complete" });

            // UpdateTask is never invoked in this scenario (there are no input requests to resolve)
            // but must be present so the handler-configuration validation passes.
            options.Handlers.UpdateTaskHandler = (context, cancellationToken) =>
                new ValueTask<UpdateTaskResult>(new UpdateTaskResult { ResultType = "complete" });
        });
    }

    [Fact]
    public async Task CallToolAsync_TaskStuckInInputRequired_WithoutNewRequests_ThrowsAfterThreshold()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var ex = await Assert.ThrowsAsync<McpException>(async () =>
            await client.CallToolAsync(new CallToolRequestParams { Name = "any-tool" }, ct));

        Assert.Contains(McpTaskStatus.InputRequired.ToString(), ex.Message);
        Assert.Contains("consecutive polls", ex.Message);

        Assert.Equal(60, _pollCount);
    }

    [Fact]
    public async Task CallToolAsync_StuckDetector_HonorsConfiguredThreshold()
    {
        // Verifies McpClientOptions.MaxConsecutiveStuckPolls is plumbed into PollTaskToCompletionAsync:
        // a smaller configured threshold is surfaced verbatim in the McpException message.
        const int CustomThreshold = 3;

        await using var client = await CreateMcpClientForServer(new McpClientOptions
        {
            MaxConsecutiveStuckPolls = CustomThreshold,
        });
        var ct = TestContext.Current.CancellationToken;

        var ex = await Assert.ThrowsAsync<McpException>(async () =>
            await client.CallToolAsync(new CallToolRequestParams { Name = "any-tool" }, ct));

        // The message embeds the configured threshold, which is the strongest signal that the
        // option value (not the 60-default constant) is what governed the loop.
        Assert.Contains($"{CustomThreshold} consecutive polls", ex.Message);
        Assert.Equal(CustomThreshold, _pollCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void McpClientOptions_MaxConsecutiveStuckPolls_RejectsNonPositive(int value)
    {
        var options = new McpClientOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxConsecutiveStuckPolls = value);
    }

    [Fact]
    public void McpClientOptions_MaxConsecutiveStuckPolls_DefaultsTo60()
    {
        Assert.Equal(60, new McpClientOptions().MaxConsecutiveStuckPolls);
    }
}
