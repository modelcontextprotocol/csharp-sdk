using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Client;

public class McpClientTaskMethodsTests : ClientServerTestBase
{
    public McpClientTaskMethodsTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        // Add task store for server-side task support
        var taskStore = new InMemoryMcpTaskStore();
        services.AddSingleton<IMcpTaskStore>(taskStore);

        // Configure server to use the task store directly
        services.Configure<McpServerOptions>(options =>
        {
            options.TaskStore = taskStore;
        });

        // Add a simple tool for testing
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

    private static IDictionary<string, JsonElement> CreateArguments(string key, object? value)
    {
        // For simple strings, just create a JsonElement from a string value
        return new Dictionary<string, JsonElement>
        {
            [key] = JsonDocument.Parse($"\"{value}\"").RootElement.Clone()
        };
    }

    [Fact]
    public async Task GetTaskAsync_ReturnsTaskStatus()
    {
        await using McpClient client = await CreateMcpClientForServer();

        // Create a task by calling a tool with task metadata
        var callResult = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "test-tool",
                Arguments = CreateArguments("input", "test"),
                Task = new McpTaskMetadata()
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // The response should contain task metadata
        Assert.NotNull(callResult.Task);
        
        string taskId = callResult.Task.TaskId;

        // Now get the task status
        var task = await client.GetTaskAsync(taskId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(taskId, task.TaskId);
    }

    [Fact]
    public async Task GetTaskAsync_ThrowsForInvalidTaskId()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.GetTaskAsync("", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetTaskResultAsync_ReturnsDeserializedResult()
    {
        await using McpClient client = await CreateMcpClientForServer();

        // Create a task
        var callResult = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "test-tool",
                Arguments = CreateArguments("input", "hello"),
                Task = new McpTaskMetadata()
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(callResult.Task);
        string taskId = callResult.Task.TaskId;

        // Wait for task to complete and get the result
        JsonElement result = await client.GetTaskResultAsync(taskId, cancellationToken: TestContext.Current.CancellationToken);

        // Verify the result has the expected CallToolResult shape
        CallToolResult? toolResult = result.Deserialize<CallToolResult>(McpJsonUtilities.DefaultOptions);
        Assert.NotNull(toolResult);
        Assert.NotEmpty(toolResult.Content);
        
        TextContentBlock? textContent = toolResult.Content[0] as TextContentBlock;
        Assert.NotNull(textContent);
        Assert.Equal("Processed: hello", textContent.Text);
    }

    [Fact]
    public async Task GetTaskResultAsync_ThrowsForInvalidTaskId()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.GetTaskResultAsync("", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListTasksAsync_ReturnsTasks()
    {
        await using McpClient client = await CreateMcpClientForServer();

        // Create a task
        var callResult = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "test-tool",
                Arguments = CreateArguments("input", "test"),
                Task = new McpTaskMetadata()
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(callResult.Task);
        string taskId = callResult.Task.TaskId;

        // List all tasks
        var tasks = await client.ListTasksAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(tasks);
        Assert.Contains(tasks, t => t.TaskId == taskId);
    }

    [Fact]
    public async Task ListTasksAsync_HandlesEmptyResult()
    {
        await using McpClient client = await CreateMcpClientForServer();

        // List tasks (may or may not be empty depending on state)
        var tasks = await client.ListTasksAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(tasks);
    }

    [Fact]
    public async Task ListTasksAsync_LowLevel_ReturnsRawResult()
    {
        await using McpClient client = await CreateMcpClientForServer();

        // Create a task first
        await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "test-tool",
                Arguments = CreateArguments("input", "task1"),
                Task = new McpTaskMetadata()
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // Use low-level API
        var result = await client.ListTasksAsync(new ListTasksRequestParams(), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(result.Tasks);
    }

    [Fact]
    public async Task ListTasksAsync_LowLevel_ThrowsForNullParams()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await client.ListTasksAsync((ListTasksRequestParams)null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancelTaskAsync_CancelsRunningTask()
    {
        await using McpClient client = await CreateMcpClientForServer();

        // Create a task
        var callResult = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "test-tool",
                Arguments = CreateArguments("input", "test"),
                Task = new McpTaskMetadata()
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(callResult.Task);
        string taskId = callResult.Task.TaskId;

        // Cancel the task
        var canceledTask = await client.CancelTaskAsync(taskId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(taskId, canceledTask.TaskId);
    }

    [Fact]
    public async Task CancelTaskAsync_ThrowsForInvalidTaskId()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.CancelTaskAsync("", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListTasksAsync_HandlesPagination()
    {
        await using McpClient client = await CreateMcpClientForServer();

        // Create multiple tasks
        var taskIds = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var result = await client.CallToolAsync(
                new CallToolRequestParams
                {
                    Name = "test-tool",
                    Arguments = CreateArguments("input", $"task-{i}"),
                    Task = new McpTaskMetadata()
                },
                cancellationToken: TestContext.Current.CancellationToken);
            
            Assert.NotNull(result.Task);
            taskIds.Add(result.Task.TaskId);
        }

        // List all tasks (should handle pagination automatically if needed)
        var tasks = await client.ListTasksAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(tasks);
        Assert.True(tasks.Count >= taskIds.Count, "Should retrieve at least the tasks we created");

        // Verify all our tasks are in the result
        foreach (var taskId in taskIds)
        {
            Assert.Contains(tasks, t => t.TaskId == taskId);
        }
    }
}
