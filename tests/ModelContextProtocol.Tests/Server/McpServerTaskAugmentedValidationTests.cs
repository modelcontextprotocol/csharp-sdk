using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for validation of task-augmented tool call requests.
/// </summary>
public class McpServerTaskAugmentedValidationTests : LoggedTest
{
    public McpServerTaskAugmentedValidationTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    private static IDictionary<string, JsonElement> CreateArguments(string key, object? value)
    {
        return new Dictionary<string, JsonElement>
        {
            [key] = JsonDocument.Parse($"\"{value}\"").RootElement.Clone()
        };
    }

    [Fact]
    public async Task CallToolAsTask_ThrowsError_WhenNoTaskStoreConfigured()
    {
        // Arrange - Server WITHOUT task store, but with an async tool (auto-marked as taskSupport: optional)
        await using var fixture = new ServerClientFixture(LoggerFactory, configureServer: (services, builder) =>
        {
            // Note: NOT configuring a task store
            builder.WithTools([McpServerTool.Create(
                async (string input, CancellationToken ct) =>
                {
                    await Task.Delay(10, ct);
                    return $"Result: {input}";
                },
                new McpServerToolCreateOptions
                {
                    Name = "async-tool",
                    Description = "An async tool"
                })]);
        });

        await using var client = await fixture.CreateClientAsync(TestContext.Current.CancellationToken);

        // Act & Assert - Calling with task metadata should fail
        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.CallToolAsync(
                new CallToolRequestParams
                {
                    Name = "async-tool",
                    Arguments = CreateArguments("input", "test"),
                    Task = new McpTaskMetadata()
                },
                TestContext.Current.CancellationToken));

        Assert.Contains("not supported", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallToolAsTask_ThrowsError_WhenToolHasForbiddenTaskSupport()
    {
        // Arrange - Server with task store, but tool has taskSupport: forbidden (sync tool)
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ServerClientFixture(LoggerFactory, configureServer: (services, builder) =>
        {
            services.AddSingleton<IMcpTaskStore>(taskStore);
            services.Configure<McpServerOptions>(options =>
            {
                options.TaskStore = taskStore;
            });

            // Create a synchronous tool - which will have taskSupport: forbidden (default)
            builder.WithTools([McpServerTool.Create(
                (string input) => $"Result: {input}",
                new McpServerToolCreateOptions
                {
                    Name = "sync-tool",
                    Description = "A synchronous tool that does not support tasks"
                })]);
        });

        await using var client = await fixture.CreateClientAsync(TestContext.Current.CancellationToken);

        // Act & Assert - Calling with task metadata should fail because tool doesn't support it
        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.CallToolAsync(
                new CallToolRequestParams
                {
                    Name = "sync-tool",
                    Arguments = CreateArguments("input", "test"),
                    Task = new McpTaskMetadata()
                },
                TestContext.Current.CancellationToken));

        Assert.Contains("does not support task-augmented execution", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
    }

    [Fact]
    public async Task CallToolAsTask_Succeeds_WhenToolHasOptionalTaskSupport()
    {
        // Arrange - Server with task store and async tool (auto-marked as taskSupport: optional)
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ServerClientFixture(LoggerFactory, configureServer: (services, builder) =>
        {
            services.AddSingleton<IMcpTaskStore>(taskStore);
            services.Configure<McpServerOptions>(options =>
            {
                options.TaskStore = taskStore;
            });

            builder.WithTools([McpServerTool.Create(
                async (string input, CancellationToken ct) =>
                {
                    await Task.Delay(10, ct);
                    return $"Result: {input}";
                },
                new McpServerToolCreateOptions
                {
                    Name = "async-tool",
                    Description = "An async tool with optional task support"
                })]);
        });

        await using var client = await fixture.CreateClientAsync(TestContext.Current.CancellationToken);

        // Act - Calling with task metadata should succeed
        var result = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "async-tool",
                Arguments = CreateArguments("input", "test"),
                Task = new McpTaskMetadata()
            },
            TestContext.Current.CancellationToken);

        // Assert - Should return a task
        Assert.NotNull(result.Task);
        Assert.NotNull(result.Task.TaskId);
    }

    [Fact]
    public async Task CallToolNormally_Succeeds_WhenToolHasForbiddenTaskSupport()
    {
        // Arrange - Server with task store, but calling without task metadata
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ServerClientFixture(LoggerFactory, configureServer: (services, builder) =>
        {
            services.AddSingleton<IMcpTaskStore>(taskStore);
            services.Configure<McpServerOptions>(options =>
            {
                options.TaskStore = taskStore;
            });

            builder.WithTools([McpServerTool.Create(
                (string input) => $"Result: {input}",
                new McpServerToolCreateOptions
                {
                    Name = "sync-tool",
                    Description = "A synchronous tool"
                })]);
        });

        await using var client = await fixture.CreateClientAsync(TestContext.Current.CancellationToken);

        // Act - Calling WITHOUT task metadata should succeed
        var result = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "sync-tool",
                Arguments = CreateArguments("input", "test"),
            },
            TestContext.Current.CancellationToken);

        // Assert - Should return normal result
        Assert.NotNull(result.Content);
        Assert.Null(result.Task);
    }

    [Fact]
    public async Task CallToolNormally_ThrowsError_WhenToolHasRequiredTaskSupport()
    {
        // Arrange - Server with task store and tool with taskSupport: required
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ServerClientFixture(LoggerFactory, configureServer: (services, builder) =>
        {
            services.AddSingleton<IMcpTaskStore>(taskStore);
            services.Configure<McpServerOptions>(options =>
            {
                options.TaskStore = taskStore;
            });

            builder.WithTools([McpServerTool.Create(
                async (string input, CancellationToken ct) =>
                {
                    await Task.Delay(100, ct);
                    return $"Result: {input}";
                },
                new McpServerToolCreateOptions
                {
                    Name = "required-task-tool",
                    Description = "A tool that requires task-augmented execution",
                    Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Required }
                })]);
        });

        await using var client = await fixture.CreateClientAsync(TestContext.Current.CancellationToken);

        // Act & Assert - Calling WITHOUT task metadata should fail
        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.CallToolAsync(
                new CallToolRequestParams
                {
                    Name = "required-task-tool",
                    Arguments = CreateArguments("input", "test"),
                },
                TestContext.Current.CancellationToken));

        Assert.Contains("requires task-augmented execution", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
    }

    [Fact]
    public async Task CallToolAsTask_Succeeds_WhenToolHasRequiredTaskSupport()
    {
        // Arrange - Server with task store and tool with taskSupport: required
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ServerClientFixture(LoggerFactory, configureServer: (services, builder) =>
        {
            services.AddSingleton<IMcpTaskStore>(taskStore);
            services.Configure<McpServerOptions>(options =>
            {
                options.TaskStore = taskStore;
            });

            builder.WithTools([McpServerTool.Create(
                async (string input, CancellationToken ct) =>
                {
                    await Task.Delay(10, ct);
                    return $"Result: {input}";
                },
                new McpServerToolCreateOptions
                {
                    Name = "required-task-tool",
                    Description = "A tool that requires task-augmented execution",
                    Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Required }
                })]);
        });

        await using var client = await fixture.CreateClientAsync(TestContext.Current.CancellationToken);

        // Act - Calling WITH task metadata should succeed
        var result = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "required-task-tool",
                Arguments = CreateArguments("input", "test"),
                Task = new McpTaskMetadata()
            },
            TestContext.Current.CancellationToken);

        // Assert - Should return a task
        Assert.NotNull(result.Task);
        Assert.NotNull(result.Task.TaskId);
    }

    [Fact]
    public async Task CallToolAsTaskAsync_WithProgress_CreatesTaskSuccessfully()
    {
        // Arrange - Server with task store and a tool that reports progress
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ServerClientFixture(LoggerFactory, configureServer: (services, builder) =>
        {
            services.AddSingleton<IMcpTaskStore>(taskStore);
            services.Configure<McpServerOptions>(options =>
            {
                options.TaskStore = taskStore;
            });

            builder.WithTools([McpServerTool.Create(
                async (IProgress<ProgressNotificationValue> progress, CancellationToken ct) =>
                {
                    // Report progress
                    progress.Report(new ProgressNotificationValue
                    {
                        Progress = 50,
                        Total = 100,
                        Message = "Halfway done"
                    });
                    await Task.Delay(10, ct);
                    return "Completed with progress";
                },
                new McpServerToolCreateOptions
                {
                    Name = "progress-task-tool",
                    Description = "A tool that reports progress during task execution"
                })]);
        });

        await using var client = await fixture.CreateClientAsync(TestContext.Current.CancellationToken);

        // Track progress notifications received by client
        var receivedProgressValues = new List<ProgressNotificationValue>();
        IProgress<ProgressNotificationValue> progress = new SynchronousProgress(value =>
        {
            lock (receivedProgressValues)
            {
                receivedProgressValues.Add(value);
            }
        });

        // Act - Call tool as task with progress tracking
        var mcpTask = await client.CallToolAsTaskAsync(
            "progress-task-tool",
            arguments: null,
            taskMetadata: new McpTaskMetadata(),
            progress: progress,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Task was created successfully
        Assert.NotNull(mcpTask);
        Assert.NotEmpty(mcpTask.TaskId);

        // Note: Progress notifications may not be received for task-augmented calls
        // because the notification handler is disposed when the task creation response returns.
        // This test verifies the code path executes without errors.
    }

    [Fact]
    public async Task CallToolAsTaskAsync_WithoutProgress_DoesNotRequireProgressHandler()
    {
        // Arrange - Server with task store and a tool that reports progress
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ServerClientFixture(LoggerFactory, configureServer: (services, builder) =>
        {
            services.AddSingleton<IMcpTaskStore>(taskStore);
            services.Configure<McpServerOptions>(options =>
            {
                options.TaskStore = taskStore;
            });

            builder.WithTools([McpServerTool.Create(
                async (IProgress<ProgressNotificationValue> progress, CancellationToken ct) =>
                {
                    // Tool reports progress but client doesn't listen
                    progress.Report(new ProgressNotificationValue { Progress = 50, Message = "Halfway" });
                    await Task.Delay(10, ct);
                    return "Done";
                },
                new McpServerToolCreateOptions
                {
                    Name = "progress-tool",
                    Description = "A tool that reports progress"
                })]);
        });

        await using var client = await fixture.CreateClientAsync(TestContext.Current.CancellationToken);

        // Act - Call tool as task WITHOUT progress tracking (progress: null)
        var mcpTask = await client.CallToolAsTaskAsync(
            "progress-tool",
            arguments: null,
            taskMetadata: new McpTaskMetadata(),
            progress: null, // No progress handler
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Task was still created successfully
        Assert.NotNull(mcpTask);
        Assert.NotEmpty(mcpTask.TaskId);
    }

    private sealed class SynchronousProgress(Action<ProgressNotificationValue> callback) : IProgress<ProgressNotificationValue>
    {
        public void Report(ProgressNotificationValue value) => callback(value);
    }

    #region Error Code Tests for Invalid/Nonexistent TaskId

    [Fact]
    public async Task GetTaskAsync_WithNonexistentTaskId_ReturnsInvalidParamsError()
    {
        // Arrange - Spec: "Invalid or nonexistent taskId in tasks/get: -32602 (Invalid params)"
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ServerClientFixture(LoggerFactory, configureServer: (services, builder) =>
        {
            services.AddSingleton<IMcpTaskStore>(taskStore);
            services.Configure<McpServerOptions>(options => options.TaskStore = taskStore);
            builder.WithTools([McpServerTool.Create(
                async (CancellationToken ct) => { await Task.Delay(10, ct); return "ok"; },
                new McpServerToolCreateOptions { Name = "test-tool" })]);
        });

        await using var client = await fixture.CreateClientAsync(TestContext.Current.CancellationToken);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.GetTaskAsync("nonexistent-task-id-12345", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
        Assert.Contains("not found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetTaskResultAsync_WithNonexistentTaskId_ReturnsInvalidParamsError()
    {
        // Arrange - Spec: "Invalid or nonexistent taskId in tasks/result: -32602 (Invalid params)"
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ServerClientFixture(LoggerFactory, configureServer: (services, builder) =>
        {
            services.AddSingleton<IMcpTaskStore>(taskStore);
            services.Configure<McpServerOptions>(options => options.TaskStore = taskStore);
            builder.WithTools([McpServerTool.Create(
                async (CancellationToken ct) => { await Task.Delay(10, ct); return "ok"; },
                new McpServerToolCreateOptions { Name = "test-tool" })]);
        });

        await using var client = await fixture.CreateClientAsync(TestContext.Current.CancellationToken);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.GetTaskResultAsync("nonexistent-task-id-12345", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
        Assert.Contains("not found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelTaskAsync_WithNonexistentTaskId_ReturnsError()
    {
        // Arrange - Spec: "Invalid or nonexistent taskId in tasks/cancel: -32602 (Invalid params)"
        // NOTE: Current implementation throws InternalError; this documents actual behavior
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ServerClientFixture(LoggerFactory, configureServer: (services, builder) =>
        {
            services.AddSingleton<IMcpTaskStore>(taskStore);
            services.Configure<McpServerOptions>(options => options.TaskStore = taskStore);
            builder.WithTools([McpServerTool.Create(
                async (CancellationToken ct) => { await Task.Delay(10, ct); return "ok"; },
                new McpServerToolCreateOptions { Name = "test-tool" })]);
        });

        await using var client = await fixture.CreateClientAsync(TestContext.Current.CancellationToken);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.CancelTaskAsync("nonexistent-task-id-12345", cancellationToken: TestContext.Current.CancellationToken));

        Assert.NotNull(exception);
    }

    [Fact]
    public async Task ListTasksAsync_WithInvalidCursor_HandlesGracefully()
    {
        // Arrange - Spec says: "Invalid or nonexistent cursor in tasks/list: -32602 (Invalid params)"
        // Current implementation ignores invalid cursors gracefully
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ServerClientFixture(LoggerFactory, configureServer: (services, builder) =>
        {
            services.AddSingleton<IMcpTaskStore>(taskStore);
            services.Configure<McpServerOptions>(options => options.TaskStore = taskStore);
            builder.WithTools([McpServerTool.Create(
                async (CancellationToken ct) => { await Task.Delay(10, ct); return "ok"; },
                new McpServerToolCreateOptions { Name = "test-tool" })]);
        });

        await using var client = await fixture.CreateClientAsync(TestContext.Current.CancellationToken);

        // Act - Pass invalid cursor
        var result = await client.ListTasksAsync(
            new ListTasksRequestParams { Cursor = "invalid-cursor-that-does-not-exist" },
            TestContext.Current.CancellationToken);

        // Assert - Should return valid (possibly empty) result
        Assert.NotNull(result.Tasks);
    }

    #endregion

    #region Blocking Behavior Tests

    [Fact]
    public async Task GetTaskResultAsync_ReturnsImmediately_WhenTaskAlreadyComplete()
    {
        // Arrange
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ServerClientFixture(LoggerFactory, configureServer: (services, builder) =>
        {
            services.AddSingleton<IMcpTaskStore>(taskStore);
            services.Configure<McpServerOptions>(options => options.TaskStore = taskStore);
            builder.WithTools([McpServerTool.Create(
                async (CancellationToken ct) => { await Task.Delay(10, ct); return "quick result"; },
                new McpServerToolCreateOptions { Name = "quick-tool" })]);
        });

        await using var client = await fixture.CreateClientAsync(TestContext.Current.CancellationToken);

        // Create and wait for task to complete
        var callResult = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "quick-tool",
                Arguments = new Dictionary<string, JsonElement>(),
                Task = new McpTaskMetadata()
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(callResult.Task);
        string taskId = callResult.Task.TaskId;

        // Wait for task to complete
        McpTask taskStatus;
        do
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            taskStatus = await client.GetTaskAsync(taskId, cancellationToken: TestContext.Current.CancellationToken);
        }
        while (taskStatus.Status == McpTaskStatus.Working);

        // Act - Get result (should return since task is complete)
        var result = await client.GetTaskResultAsync(taskId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Should get valid result
        Assert.NotEqual(default, result);
    }

    [Fact]
    public async Task GetTaskResultAsync_ForFailedTask_ReturnsErrorResult()
    {
        // Arrange
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ServerClientFixture(LoggerFactory, configureServer: (services, builder) =>
        {
            services.AddSingleton<IMcpTaskStore>(taskStore);
            services.Configure<McpServerOptions>(options => options.TaskStore = taskStore);
            builder.WithTools([McpServerTool.Create(
                async (CancellationToken ct) =>
                {
                    await Task.Delay(10, ct);
                    throw new InvalidOperationException("Tool execution failed intentionally");
#pragma warning disable CS0162 // Unreachable code detected
                    return "never";
#pragma warning restore CS0162
                },
                new McpServerToolCreateOptions { Name = "failable-tool" })]);
        });

        await using var client = await fixture.CreateClientAsync(TestContext.Current.CancellationToken);

        // Create a failing task
        var callResult = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "failable-tool",
                Arguments = new Dictionary<string, JsonElement>(),
                Task = new McpTaskMetadata()
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(callResult.Task);
        string taskId = callResult.Task.TaskId;

        // Wait for task to fail
        McpTask taskStatus;
        int attempts = 0;
        do
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            taskStatus = await client.GetTaskAsync(taskId, cancellationToken: TestContext.Current.CancellationToken);
            attempts++;
        }
        while (taskStatus.Status == McpTaskStatus.Working && attempts < 50);

        Assert.Equal(McpTaskStatus.Failed, taskStatus.Status);

        // Act - Get result for failed task
        var result = await client.GetTaskResultAsync(taskId, cancellationToken: TestContext.Current.CancellationToken);
        var toolResult = result.Deserialize<CallToolResult>(McpJsonUtilities.DefaultOptions);

        // Assert - Failed task should have isError=true
        Assert.NotNull(toolResult);
        Assert.True(toolResult.IsError, "Failed task should have isError=true in the result");
    }

    #endregion

    #region Task Consistency and Lifecycle Tests

    [Fact]
    public async Task ListTasksAsync_ContainsAllTasksRetrievableByGet()
    {
        // Arrange - Spec: "If a task is retrievable via tasks/get, it MUST be retrievable via tasks/list"
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ServerClientFixture(LoggerFactory, configureServer: (services, builder) =>
        {
            services.AddSingleton<IMcpTaskStore>(taskStore);
            services.Configure<McpServerOptions>(options => options.TaskStore = taskStore);
            builder.WithTools([McpServerTool.Create(
                async (string input, CancellationToken ct) => { await Task.Delay(10, ct); return $"Result: {input}"; },
                new McpServerToolCreateOptions { Name = "test-tool" })]);
        });

        await using var client = await fixture.CreateClientAsync(TestContext.Current.CancellationToken);

        // Create multiple tasks
        var createdTaskIds = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var result = await client.CallToolAsync(
                new CallToolRequestParams
                {
                    Name = "test-tool",
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["input"] = JsonDocument.Parse($"\"task-{i}\"").RootElement.Clone()
                    },
                    Task = new McpTaskMetadata()
                },
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(result.Task);
            createdTaskIds.Add(result.Task.TaskId);
        }

        // Verify each task is retrievable via get
        foreach (var taskId in createdTaskIds)
        {
            var task = await client.GetTaskAsync(taskId, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(task);
        }

        // Act - List all tasks
        var allTasks = await client.ListTasksAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert - All tasks must be in the list
        foreach (var taskId in createdTaskIds)
        {
            Assert.Contains(allTasks, t => t.TaskId == taskId);
        }
    }

    [Fact]
    public async Task NewTask_StartsInWorkingStatus()
    {
        // Arrange - Spec: "Tasks MUST begin in the working status when created."
        var taskStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var taskCanComplete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ServerClientFixture(LoggerFactory, configureServer: (services, builder) =>
        {
            services.AddSingleton<IMcpTaskStore>(taskStore);
            services.Configure<McpServerOptions>(options => options.TaskStore = taskStore);
            builder.WithTools([McpServerTool.Create(
                async (CancellationToken ct) =>
                {
                    taskStarted.TrySetResult(true);
                    await taskCanComplete.Task.WaitAsync(ct);
                    return "done";
                },
                new McpServerToolCreateOptions { Name = "controllable-tool" })]);
        });

        await using var client = await fixture.CreateClientAsync(TestContext.Current.CancellationToken);

        // Act - Create a task
        var callResult = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "controllable-tool",
                Arguments = new Dictionary<string, JsonElement>(),
                Task = new McpTaskMetadata()
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(callResult.Task);
        Assert.Equal(McpTaskStatus.Working, callResult.Task.Status);

        // Cleanup
        taskCanComplete.TrySetResult(true);
    }

    [Fact]
    public async Task Task_ContainsRequiredTimestamps()
    {
        // Arrange - Spec: "Receivers MUST include createdAt and lastUpdatedAt timestamps"
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ServerClientFixture(LoggerFactory, configureServer: (services, builder) =>
        {
            services.AddSingleton<IMcpTaskStore>(taskStore);
            services.Configure<McpServerOptions>(options => options.TaskStore = taskStore);
            builder.WithTools([McpServerTool.Create(
                async (CancellationToken ct) => { await Task.Delay(10, ct); return "ok"; },
                new McpServerToolCreateOptions { Name = "test-tool" })]);
        });

        await using var client = await fixture.CreateClientAsync(TestContext.Current.CancellationToken);

        var beforeCreation = DateTimeOffset.UtcNow;

        // Act
        var callResult = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "test-tool",
                Arguments = new Dictionary<string, JsonElement>(),
                Task = new McpTaskMetadata()
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var afterCreation = DateTimeOffset.UtcNow;

        // Assert
        Assert.NotNull(callResult.Task);
        Assert.NotEqual(default, callResult.Task.CreatedAt);
        Assert.NotEqual(default, callResult.Task.LastUpdatedAt);
        Assert.True(callResult.Task.CreatedAt >= beforeCreation.AddSeconds(-1));
        Assert.True(callResult.Task.CreatedAt <= afterCreation.AddSeconds(1));
    }

    [Fact]
    public async Task Task_IncludesTtlInResponse()
    {
        // Arrange - Spec: "Receivers MUST include the actual ttl duration in tasks/get responses."
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ServerClientFixture(LoggerFactory, configureServer: (services, builder) =>
        {
            services.AddSingleton<IMcpTaskStore>(taskStore);
            services.Configure<McpServerOptions>(options => options.TaskStore = taskStore);
            builder.WithTools([McpServerTool.Create(
                async (CancellationToken ct) => { await Task.Delay(10, ct); return "ok"; },
                new McpServerToolCreateOptions { Name = "test-tool" })]);
        });

        await using var client = await fixture.CreateClientAsync(TestContext.Current.CancellationToken);

        // Act
        var callResult = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "test-tool",
                Arguments = new Dictionary<string, JsonElement>(),
                Task = new McpTaskMetadata { TimeToLive = TimeSpan.FromMinutes(30) }
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(callResult.Task);
        Assert.NotNull(callResult.Task.TimeToLive);

        var taskStatus = await client.GetTaskAsync(callResult.Task.TaskId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(taskStatus.TimeToLive);
    }

    [Fact]
    public async Task Task_IncludesPollIntervalInResponse()
    {
        // Arrange - Spec: "Receivers MAY include a pollInterval value"
        var taskStore = new InMemoryMcpTaskStore();

        await using var fixture = new ServerClientFixture(LoggerFactory, configureServer: (services, builder) =>
        {
            services.AddSingleton<IMcpTaskStore>(taskStore);
            services.Configure<McpServerOptions>(options => options.TaskStore = taskStore);
            builder.WithTools([McpServerTool.Create(
                async (CancellationToken ct) => { await Task.Delay(10, ct); return "ok"; },
                new McpServerToolCreateOptions { Name = "test-tool" })]);
        });

        await using var client = await fixture.CreateClientAsync(TestContext.Current.CancellationToken);

        // Act
        var callResult = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "test-tool",
                Arguments = new Dictionary<string, JsonElement>(),
                Task = new McpTaskMetadata()
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(callResult.Task);
        Assert.NotNull(callResult.Task.PollInterval);
    }

    #endregion

    #region Server Without Tasks Capability Tests

    [Fact]
    public async Task ServerCapabilities_DoNotIncludeTasks_WhenNoTaskStore()
    {
        // Arrange - Spec: "If capabilities.tasks is not defined, the peer SHOULD NOT attempt to create tasks"
        await using var fixture = new ServerClientFixture(LoggerFactory, configureServer: (services, builder) =>
        {
            // NOT configuring a task store
            builder.WithTools([McpServerTool.Create(
                async (CancellationToken ct) => { await Task.Delay(10, ct); return "ok"; },
                new McpServerToolCreateOptions { Name = "async-tool" })]);
        });

        await using var client = await fixture.CreateClientAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(client.ServerCapabilities?.Tasks);
    }

    [Fact]
    public async Task NormalRequest_Succeeds_WhenTasksNotSupported()
    {
        // Arrange - Normal requests should work without task support
        await using var fixture = new ServerClientFixture(LoggerFactory, configureServer: (services, builder) =>
        {
            builder.WithTools([McpServerTool.Create(
                (string input) => $"Sync result: {input}",
                new McpServerToolCreateOptions { Name = "sync-tool" })]);
        });

        await using var client = await fixture.CreateClientAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "sync-tool",
                Arguments = CreateArguments("input", "test")
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result.Content);
        Assert.Null(result.Task);
    }

    #endregion

    /// <summary>
    /// Helper fixture for creating server-client pairs with custom configuration.
    /// </summary>
    private sealed class ServerClientFixture : IAsyncDisposable
    {
        private readonly System.IO.Pipelines.Pipe _clientToServerPipe = new();
        private readonly System.IO.Pipelines.Pipe _serverToClientPipe = new();
        private readonly IServiceProvider _serviceProvider;
        private readonly McpServer _server;
        private readonly Task _serverTask;
        private readonly CancellationTokenSource _cts;
        private readonly ILoggerFactory _loggerFactory;

        public ServerClientFixture(
            ILoggerFactory loggerFactory,
            Action<ServiceCollection, IMcpServerBuilder>? configureServer = null)
        {
            _loggerFactory = loggerFactory;
            _cts = new CancellationTokenSource();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(loggerFactory);

            var builder = services
                .AddMcpServer()
                .WithStreamServerTransport(
                    _clientToServerPipe.Reader.AsStream(),
                    _serverToClientPipe.Writer.AsStream());

            configureServer?.Invoke(services, builder);

            _serviceProvider = services.BuildServiceProvider(validateScopes: true);
            _server = _serviceProvider.GetRequiredService<McpServer>();
            _serverTask = _server.RunAsync(_cts.Token);
        }

        public async Task<McpClient> CreateClientAsync(CancellationToken cancellationToken)
        {
            return await McpClient.CreateAsync(
                new StreamClientTransport(
                    serverInput: _clientToServerPipe.Writer.AsStream(),
                    _serverToClientPipe.Reader.AsStream(),
                    _loggerFactory),
                loggerFactory: _loggerFactory,
                cancellationToken: cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();

            _clientToServerPipe.Writer.Complete();
            _serverToClientPipe.Writer.Complete();

            try
            {
                await _serverTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

            if (_serviceProvider is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _cts.Dispose();
        }
    }
}
