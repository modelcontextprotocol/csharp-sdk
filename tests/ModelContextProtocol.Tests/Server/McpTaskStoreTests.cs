using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;
using System.Text.Json;

#pragma warning disable MCPEXP001

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for the <see cref="IMcpTaskStore"/>-based auto-wiring of tools/call into tasks.
/// Verifies that setting <see cref="McpServerOptions.TaskStore"/> enables task support
/// for <see cref="McpServerTool"/>-based tools.
/// </summary>
public class McpTaskStoreTests : ClientServerTestBase
{
    public McpTaskStoreTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
#if !NET
        Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "https://github.com/modelcontextprotocol/csharp-sdk/issues/587");
#endif
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithTools<TaskStoreTestTools>();

        mcpServerBuilder.Services.Configure<McpServerOptions>(options =>
        {
            options.TaskStore = new InMemoryMcpTaskStore
            {
                DefaultPollIntervalMs = 50,
            };
        });
    }

    [Fact]
    public async Task CallToolRawAsync_WithTaskCapability_ReturnsCreateTaskResult()
    {
        await using var client = await CreateMcpClientForServer();

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "slow-tool" },
            TestContext.Current.CancellationToken);

        // Because the client signals task support and a TaskStore is configured,
        // the server should wrap the tool execution in a task.
        Assert.True(augmented.IsTask);
        Assert.NotNull(augmented.TaskCreated);
        Assert.Equal(McpTaskStatus.Working, augmented.TaskCreated.Status);
    }

    [Fact]
    public async Task CallToolAsync_WithTaskStore_PollsToCompletion()
    {
        await using var client = await CreateMcpClientForServer();

        // CallToolAsync should poll until the background execution completes.
        var result = await client.CallToolAsync(
            new CallToolRequestParams { Name = "slow-tool" },
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.Content);
        Assert.Equal("slow result", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [Fact]
    public async Task CallToolAsync_WithTaskStore_FastTool_StillCreatesTask()
    {
        await using var client = await CreateMcpClientForServer();

        // Even a fast tool should go through the task store when the client signals capability.
        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "fast-tool" },
            TestContext.Current.CancellationToken);

        Assert.True(augmented.IsTask);
    }

    [Fact]
    public async Task GetTaskAsync_ViaStore_ReturnsCompletedResult()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "fast-tool" }, ct);

        var taskId = augmented.TaskCreated!.TaskId;

        // The fast-tool returns immediately in the background, so poll briefly
        GetTaskResult? taskResult = null;
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(50, ct);
            taskResult = await client.GetTaskAsync(taskId, ct);
            if (taskResult is CompletedTaskResult)
            {
                break;
            }
        }

        Assert.IsType<CompletedTaskResult>(taskResult);
    }

    [Fact]
    public async Task CancelTaskAsync_ViaStore_TransitionsToCancelled()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        // Create a slow task that won't complete on its own
        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "slow-tool" }, ct);

        var taskId = augmented.TaskCreated!.TaskId;

        // Cancel it
        await client.CancelTaskAsync(taskId, ct);

        // Verify state
        var taskResult = await client.GetTaskAsync(taskId, ct);
        Assert.IsType<CancelledTaskResult>(taskResult);
    }

    [Fact]
    public async Task GetTaskAsync_UnknownId_ThrowsWithInvalidParams()
    {
        await using var client = await CreateMcpClientForServer();

        var ex = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.GetTaskAsync("nonexistent-id", TestContext.Current.CancellationToken));

        Assert.Contains("Unknown task", ex.Message);
    }

    [Fact]
    public async Task ToolExecution_Failure_StoresAsCompletedWithError()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "failing-tool" }, ct);

        var taskId = augmented.TaskCreated!.TaskId;

        // Poll until completed (tool exceptions are wrapped as isError:true results)
        GetTaskResult? taskResult = null;
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(50, ct);
            taskResult = await client.GetTaskAsync(taskId, ct);
            if (taskResult is CompletedTaskResult)
            {
                break;
            }
        }

        var completed = Assert.IsType<CompletedTaskResult>(taskResult);
        // The tool result has isError: true
        Assert.True(completed.TaskResult.GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task ElicitTool_ViaTask_RedirectsThroughStore()
    {
        await using var client = await CreateMcpClientForServer(new McpClientOptions
        {
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (request, ct) =>
                {
                    // Client responds to the elicitation
                    return new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });
                }
            }
        });
        var ct = TestContext.Current.CancellationToken;

        // CallToolAsync will poll and resolve input requests automatically.
        var result = await client.CallToolAsync(
            new CallToolRequestParams { Name = "elicit-tool" }, ct);

        Assert.NotNull(result);
        Assert.Equal("accepted", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [Fact]
    public async Task SampleTool_ViaTask_RedirectsThroughStore()
    {
        await using var client = await CreateMcpClientForServer(new McpClientOptions
        {
            Handlers = new McpClientHandlers
            {
                SamplingHandler = (request, progress, ct) =>
                {
                    return new ValueTask<CreateMessageResult>(new CreateMessageResult
                    {
                        Content = [new TextContentBlock { Text = "sampled response" }],
                        Model = "test-model",
                    });
                }
            }
        });
        var ct = TestContext.Current.CancellationToken;

        var result = await client.CallToolAsync(
            new CallToolRequestParams { Name = "sample-tool" }, ct);

        Assert.NotNull(result);
        Assert.Equal("sampled response", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [Fact]
    public async Task ElicitTool_ViaTask_ClientDedups_InputRequests()
    {
        // This test verifies that the client doesn't re-resolve an input request
        // that it has already responded to in a previous poll cycle.
        int elicitCallCount = 0;

        await using var client = await CreateMcpClientForServer(new McpClientOptions
        {
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (request, ct) =>
                {
                    Interlocked.Increment(ref elicitCallCount);
                    return new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });
                }
            }
        });
        var ct = TestContext.Current.CancellationToken;

        var result = await client.CallToolAsync(
            new CallToolRequestParams { Name = "elicit-tool" }, ct);

        // The handler should be called exactly once despite potential multiple polls
        Assert.Equal(1, elicitCallCount);
        Assert.Equal("accepted", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [Fact]
    public async Task CallToolRawAsync_ElicitTool_ReturnsTask_ThenPollShowsInputRequired()
    {
        await using var client = await CreateMcpClientForServer(new McpClientOptions
        {
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (request, ct) =>
                    new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" })
            }
        });
        var ct = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "elicit-tool" }, ct);

        Assert.True(augmented.IsTask);
        var taskId = augmented.TaskCreated!.TaskId;

        // Poll — eventually the task should be input_required (elicit-tool calls ElicitAsync)
        GetTaskResult? taskResult = null;
        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(50, ct);
            taskResult = await client.GetTaskAsync(taskId, ct);
            if (taskResult is InputRequiredTaskResult)
            {
                break;
            }
        }

        Assert.IsType<InputRequiredTaskResult>(taskResult);
    }

    [McpServerToolType]
    private sealed class TaskStoreTestTools
    {
        [McpServerTool(Name = "slow-tool"), System.ComponentModel.Description("A tool that takes time")]
        public static async Task<string> SlowTool(CancellationToken cancellationToken)
        {
            await Task.Delay(200, cancellationToken);
            return "slow result";
        }

        [McpServerTool(Name = "fast-tool"), System.ComponentModel.Description("A fast tool")]
        public static string FastTool() => "fast result";

        [McpServerTool(Name = "failing-tool"), System.ComponentModel.Description("A tool that fails")]
        public static string FailingTool() => throw new InvalidOperationException("intentional failure");

        [McpServerTool(Name = "elicit-tool"), System.ComponentModel.Description("A tool that elicits")]
        public static async Task<string> ElicitTool(McpServer server, CancellationToken cancellationToken)
        {
            var result = await server.ElicitAsync(new ElicitRequestParams
            {
                Message = "What is your name?",
                RequestedSchema = new(),
            }, cancellationToken);

            return result.Action == "accept" ? "accepted" : "declined";
        }

        [McpServerTool(Name = "sample-tool"), System.ComponentModel.Description("A tool that samples")]
        public static async Task<string> SampleTool(McpServer server, CancellationToken cancellationToken)
        {
            var result = await server.SampleAsync(new CreateMessageRequestParams
            {
                Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "hello" }] }],
                MaxTokens = 100,
            }, cancellationToken);

            return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "no response";
        }
    }
}
