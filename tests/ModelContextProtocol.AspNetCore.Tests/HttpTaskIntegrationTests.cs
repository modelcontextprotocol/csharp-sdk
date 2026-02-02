using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Integration tests for MCP Tasks feature over HTTP transports.
/// Tests task creation, polling, cancellation, and result retrieval across SSE streams.
/// </summary>
public class HttpTaskIntegrationTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper)
{
    private readonly HttpClientTransportOptions DefaultTransportOptions = new()
    {
        Endpoint = new("http://localhost:5000/sse"),
        Name = "In-memory SSE Client",
    };

    private Task<McpClient> ConnectMcpClientAsync(
        HttpClient? httpClient = null,
        HttpClientTransportOptions? transportOptions = null,
        McpClientOptions? clientOptions = null)
        => McpClient.CreateAsync(
            new HttpClientTransport(transportOptions ?? DefaultTransportOptions, httpClient ?? HttpClient, LoggerFactory),
            clientOptions,
            LoggerFactory,
            TestContext.Current.CancellationToken);

    private static IDictionary<string, JsonElement> CreateArguments(string key, object? value)
    {
        return new Dictionary<string, JsonElement>
        {
            [key] = JsonSerializer.SerializeToElement(value, McpJsonUtilities.DefaultOptions)
        };
    }

    [Fact]
    public async Task CallToolAsTask_ReturnsTask_WhenServerSupportsTasksAsync()
    {
        // Arrange
        var taskStore = new InMemoryMcpTaskStore();
        Builder.Services.AddMcpServer(options =>
        {
            options.TaskStore = taskStore;
        })
        .WithHttpTransport()
        .WithTools<LongRunningTools>();

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var client = await ConnectMcpClientAsync();

        // Act - Call tool with task augmentation
        var result = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "long_running_operation",
                Arguments = CreateArguments("durationMs", 100),
                Task = new McpTaskMetadata()
            },
            TestContext.Current.CancellationToken);

        // Assert - Response should indicate task was created
        Assert.NotNull(result);
        Assert.Null(result.IsError);
    }

    [Fact]
    public async Task GetTaskAsync_ReturnsTaskStatus_WhenTaskExistsAsync()
    {
        // Arrange
        var taskStore = new InMemoryMcpTaskStore();
        Builder.Services.AddMcpServer(options =>
        {
            options.TaskStore = taskStore;
        })
        .WithHttpTransport()
        .WithTools<LongRunningTools>();

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var client = await ConnectMcpClientAsync();

        // First create a task by calling a tool with task augmentation
        _ = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "long_running_operation",
                Arguments = CreateArguments("durationMs", 500),
                Task = new McpTaskMetadata()
            },
            TestContext.Current.CancellationToken);

        // Get all tasks
        var tasks = await client.ListTasksAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(tasks);

        // Act - Get the task status
        var task = await client.GetTaskAsync(tasks[0].TaskId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(task);
        Assert.Equal(tasks[0].TaskId, task.TaskId);
    }

    [Fact]
    public async Task ListTasksAsync_ReturnsTasks_WhenTasksExistAsync()
    {
        // Arrange
        var taskStore = new InMemoryMcpTaskStore();
        Builder.Services.AddMcpServer(options =>
        {
            options.TaskStore = taskStore;
        })
        .WithHttpTransport()
        .WithTools<LongRunningTools>();

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var client = await ConnectMcpClientAsync();

        // Create multiple tasks
        for (int i = 0; i < 3; i++)
        {
            await client.CallToolAsync(
                new CallToolRequestParams
                {
                    Name = "long_running_operation",
                    Arguments = CreateArguments("durationMs", 1000),
                    Task = new McpTaskMetadata()
                },
                TestContext.Current.CancellationToken);
        }

        // Act
        var tasks = await client.ListTasksAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(tasks);
        Assert.Equal(3, tasks.Count);
    }

    [Fact]
    public async Task CancelTaskAsync_CancelsTask_WhenTaskIsRunningAsync()
    {
        // Arrange
        var taskStore = new InMemoryMcpTaskStore();
        Builder.Services.AddMcpServer(options =>
        {
            options.TaskStore = taskStore;
        })
        .WithHttpTransport()
        .WithTools<LongRunningTools>();

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var client = await ConnectMcpClientAsync();

        // Create a long-running task
        await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "long_running_operation",
                Arguments = CreateArguments("durationMs", 10000),
                Task = new McpTaskMetadata()
            },
            TestContext.Current.CancellationToken);

        var tasks = await client.ListTasksAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(tasks);

        // Act - Cancel the task
        var cancelledTask = await client.CancelTaskAsync(tasks[0].TaskId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(cancelledTask);
        Assert.Equal(McpTaskStatus.Cancelled, cancelledTask.Status);
    }

    [Fact]
    public async Task GetTaskResultAsync_ReturnsResult_WhenTaskCompletesAsync()
    {
        // Arrange
        var taskStore = new InMemoryMcpTaskStore();
        Builder.Services.AddMcpServer(options =>
        {
            options.TaskStore = taskStore;
        })
        .WithHttpTransport()
        .WithTools<LongRunningTools>();

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var client = await ConnectMcpClientAsync();

        // Create a quick task
        await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "long_running_operation",
                Arguments = CreateArguments("durationMs", 50),
                Task = new McpTaskMetadata()
            },
            TestContext.Current.CancellationToken);

        var tasks = await client.ListTasksAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(tasks);

        // Wait a bit for the task to complete
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Act - Get the task result
        var result = await client.GetTaskResultAsync(tasks[0].TaskId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(default, result);
    }

    [Fact]
    public async Task TasksIsolated_BetweenSessions_WhenMultipleClientsConnectAsync()
    {
        // Arrange
        var taskStore = new InMemoryMcpTaskStore();
        Builder.Services.AddMcpServer(options =>
        {
            options.TaskStore = taskStore;
        })
        .WithHttpTransport()
        .WithTools<LongRunningTools>();

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        // Connect two separate clients
        await using var client1 = await ConnectMcpClientAsync();
        await using var client2 = await ConnectMcpClientAsync();

        // Client 1 creates a task
        await client1.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "long_running_operation",
                Arguments = CreateArguments("durationMs", 1000),
                Task = new McpTaskMetadata()
            },
            TestContext.Current.CancellationToken);

        // Act - Both clients list tasks
        var client1Tasks = await client1.ListTasksAsync(cancellationToken: TestContext.Current.CancellationToken);
        var client2Tasks = await client2.ListTasksAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Tasks should be isolated by session
        Assert.Single(client1Tasks);
        Assert.Empty(client2Tasks);
    }

    [Fact]
    public async Task ServerCapabilities_IncludesTasks_WhenTaskStoreConfiguredAsync()
    {
        // Arrange
        var taskStore = new InMemoryMcpTaskStore();
        Builder.Services.AddMcpServer(options =>
        {
            options.TaskStore = taskStore;
        })
        .WithHttpTransport()
        .WithTools<LongRunningTools>();

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await using var client = await ConnectMcpClientAsync();

        // Assert
        Assert.NotNull(client.ServerCapabilities?.Tasks);
    }

    [Fact]
    public async Task ListTools_ShowsTaskSupport_WhenToolIsAsyncAsync()
    {
        // Arrange
        var taskStore = new InMemoryMcpTaskStore();
        Builder.Services.AddMcpServer(options =>
        {
            options.TaskStore = taskStore;
        })
        .WithHttpTransport()
        .WithTools<LongRunningTools>();

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var client = await ConnectMcpClientAsync();

        // Act
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var asyncTool = tools.FirstOrDefault(t => t.Name == "long_running_operation");
        Assert.NotNull(asyncTool);
        Assert.NotNull(asyncTool.ProtocolTool.Execution);
        Assert.Equal(ToolTaskSupport.Optional, asyncTool.ProtocolTool.Execution.TaskSupport);
    }

    [McpServerToolType]
    public sealed class LongRunningTools
    {
        [McpServerTool, Description("Simulates a long-running operation")]
        public static async Task<string> LongRunningOperation(
            [Description("Duration of the operation in milliseconds")] int durationMs,
            CancellationToken cancellationToken)
        {
            await Task.Delay(durationMs, cancellationToken);
            return $"Operation completed after {durationMs}ms";
        }

        [McpServerTool, Description("A synchronous tool that does not support tasks")]
        public static string SyncTool([Description("Input message")] string message)
        {
            return $"Sync result: {message}";
        }
    }
}
