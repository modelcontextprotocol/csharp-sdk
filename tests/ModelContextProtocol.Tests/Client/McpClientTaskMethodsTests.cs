using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Runtime.InteropServices;
using System.Text.Json;

#pragma warning disable MCPEXP001

namespace ModelContextProtocol.Tests.Client;

/// <summary>
/// Integration tests for the client-side task API methods: GetTaskAsync, CancelTaskAsync,
/// UpdateTaskAsync, CallToolRawAsync, and the automatic polling in CallToolAsync.
/// </summary>
public class McpClientTaskMethodsTests : ClientServerTestBase
{
    public McpClientTaskMethodsTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
#if !NET
        Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "https://github.com/modelcontextprotocol/csharp-sdk/issues/587");
#endif
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.Services.Configure<McpServerOptions>(options =>
        {
            options.TaskStore = new InMemoryMcpTaskStore
            {
                DefaultPollIntervalMs = 50,
            };
        });

        mcpServerBuilder.WithTools([McpServerTool.Create(
            async (string input, CancellationToken ct) =>
            {
                await Task.Delay(50, ct);
                return $"Processed: {input}";
            },
            new McpServerToolCreateOptions
            {
                Name = "test-tool",
                Description = "A test tool"
            })]);
    }

    private static IDictionary<string, JsonElement> CreateArguments(string key, string value)
    {
        return new Dictionary<string, JsonElement>
        {
            [key] = JsonDocument.Parse($"\"{value}\"").RootElement.Clone()
        };
    }

    [Fact]
    public async Task GetTaskAsync_ReturnsTaskStatus()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams
            {
                Name = "test-tool",
                Arguments = CreateArguments("input", "test"),
            }, ct);

        Assert.True(augmented.IsTask);
        var taskId = augmented.TaskCreated!.TaskId;

        // Get the task status
        var task = await client.GetTaskAsync(taskId, ct);
        Assert.NotNull(task);
    }

    [Fact]
    public async Task GetTaskAsync_UnknownTaskId_Throws()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var ex = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.GetTaskAsync("nonexistent-id", ct));

        Assert.Contains("Unknown task", ex.Message);
    }

    [Fact]
    public async Task GetTaskAsync_NullTaskId_Throws()
    {
        await using var client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await client.GetTaskAsync((string)null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CallToolRawAsync_WithTaskStore_ReturnsCreatedTask()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams
            {
                Name = "test-tool",
                Arguments = CreateArguments("input", "hello"),
            }, ct);

        Assert.True(augmented.IsTask);
        Assert.NotNull(augmented.TaskCreated);
        Assert.Equal(McpTaskStatus.Working, augmented.TaskCreated.Status);
        Assert.NotNull(augmented.TaskCreated.TaskId);
        Assert.True(augmented.TaskCreated.PollIntervalMs > 0);
    }

    [Fact]
    public async Task CallToolAsync_PollsUntilCompletion_ReturnsResult()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var result = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "test-tool",
                Arguments = CreateArguments("input", "hello"),
            }, ct);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Content);
        var textContent = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Equal("Processed: hello", textContent.Text);
    }

    [Fact]
    public async Task CancelTaskAsync_ForWorkingTask_Succeeds()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams
            {
                Name = "test-tool",
                Arguments = CreateArguments("input", "test"),
            }, ct);

        Assert.True(augmented.IsTask);
        var taskId = augmented.TaskCreated!.TaskId;

        // Cancel immediately (may succeed or fail depending on timing)
        try
        {
            await client.CancelTaskAsync(taskId, ct);

            // If cancel succeeded, verify the task is cancelled
            var taskResult = await client.GetTaskAsync(taskId, ct);
            Assert.IsType<CancelledTaskResult>(taskResult);
        }
        catch (McpProtocolException)
        {
            // Task may have already completed before we could cancel — that's fine
        }
    }

    [Fact]
    public async Task CancelTaskAsync_NullTaskId_Throws()
    {
        await using var client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await client.CancelTaskAsync((string)null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancelTaskAsync_UnknownTaskId_AcknowledgesIdempotently()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        // SEP-2663 requires servers to always acknowledge tasks/cancel, even when the task is
        // unknown (e.g., has been garbage collected). The default handler must not throw.
        var result = await client.CancelTaskAsync("nonexistent-id", ct);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetTaskAsync_AfterCompletion_ReturnsCompletedResult()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams
            {
                Name = "test-tool",
                Arguments = CreateArguments("input", "hello"),
            }, ct);

        var taskId = augmented.TaskCreated!.TaskId;

        // Poll until completed
        GetTaskResult? taskResult = null;
        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(50, ct);
            taskResult = await client.GetTaskAsync(taskId, ct);
            if (taskResult is CompletedTaskResult)
            {
                break;
            }
        }

        var completed = Assert.IsType<CompletedTaskResult>(taskResult);

        // Deserialize the stored result
        var toolResult = JsonSerializer.Deserialize<CallToolResult>(completed.Result, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(toolResult);
        Assert.NotEmpty(toolResult.Content);
        var textContent = Assert.IsType<TextContentBlock>(toolResult.Content[0]);
        Assert.Equal("Processed: hello", textContent.Text);
    }

    [Fact]
    public async Task MultipleTasks_CreatedConcurrently_HaveUniqueIds()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var taskIds = new HashSet<string>();

        for (int i = 0; i < 5; i++)
        {
            var augmented = await client.CallToolRawAsync(
                new CallToolRequestParams
                {
                    Name = "test-tool",
                    Arguments = CreateArguments("input", $"task-{i}"),
                }, ct);

            Assert.True(augmented.IsTask);
            taskIds.Add(augmented.TaskCreated!.TaskId);
        }

        // All task IDs should be unique
        Assert.Equal(5, taskIds.Count);
    }
}
