using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for McpServer methods that query tasks on the client (Phase 4 implementation).
/// </summary>
public class McpServerTaskMethodsTests : LoggedTest
{
    private readonly McpServerOptions _options;

    public McpServerTaskMethodsTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
#if !NET
        Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "https://github.com/modelcontextprotocol/csharp-sdk/issues/587");
#endif
        _options = CreateOptions();
    }

    private static McpServerOptions CreateOptions(ServerCapabilities? capabilities = null)
    {
        return new McpServerOptions
        {
            ProtocolVersion = "2024",
            InitializationTimeout = TimeSpan.FromSeconds(30),
            Capabilities = capabilities,
        };
    }

    #region SampleAsTaskAsync Tests

    [Fact]
    public async Task SampleAsTaskAsync_ThrowsException_WhenClientDoesNotSupportSampling()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities(), TestContext.Current.CancellationToken);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await server.SampleAsTaskAsync(
                new CreateMessageRequestParams { Messages = [], MaxTokens = 1000 },
                new McpTaskMetadata(),
                CancellationToken.None));

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task SampleAsTaskAsync_ThrowsException_WhenClientDoesNotSupportTaskAugmentedSampling()
    {
        // Arrange - Client supports sampling but NOT task-augmented sampling
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities
        {
            Sampling = new SamplingCapability(),
            // Note: No Tasks capability
        }, TestContext.Current.CancellationToken);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await server.SampleAsTaskAsync(
                new CreateMessageRequestParams { Messages = [], MaxTokens = 1000 },
                new McpTaskMetadata(),
                CancellationToken.None));

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task SampleAsTaskAsync_SendsRequest_WhenClientSupportsTaskAugmentedSampling()
    {
        // Arrange
        await using var transport = new TestServerTransport();

        // Configure transport to return a task result for sampling
        transport.MockTask = new McpTask
        {
            TaskId = "sample-task-123",
            Status = McpTaskStatus.Working,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
        };

        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities
        {
            Sampling = new SamplingCapability(),
            Tasks = new McpTasksCapability
            {
                Requests = new RequestMcpTasksCapability
                {
                    Sampling = new SamplingMcpTasksCapability
                    {
                        CreateMessage = new CreateMessageMcpTasksCapability()
                    }
                }
            }
        }, TestContext.Current.CancellationToken);

        // Act
        var task = await server.SampleAsTaskAsync(
            new CreateMessageRequestParams { Messages = [], MaxTokens = 1000 },
            new McpTaskMetadata { TimeToLive = TimeSpan.FromMinutes(5) },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(task);
        Assert.Equal("sample-task-123", task.TaskId);
        Assert.Equal(McpTaskStatus.Working, task.Status);

        // Verify the request was sent with task metadata
        var samplingRequest = transport.SentMessages.OfType<JsonRpcRequest>()
            .FirstOrDefault(r => r.Method == RequestMethods.SamplingCreateMessage);
        Assert.NotNull(samplingRequest);
        var requestParams = JsonSerializer.Deserialize<CreateMessageRequestParams>(
            samplingRequest.Params, McpJsonUtilities.DefaultOptions);
        Assert.NotNull(requestParams?.Task);

        await transport.DisposeAsync();
        await runTask;
    }

    #endregion

    #region ElicitAsTaskAsync Tests

    [Fact]
    public async Task ElicitAsTaskAsync_ThrowsException_WhenClientDoesNotSupportElicitation()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities(), TestContext.Current.CancellationToken);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await server.ElicitAsTaskAsync(
                new ElicitRequestParams { Message = "test", RequestedSchema = new() },
                new McpTaskMetadata(),
                CancellationToken.None));

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task ElicitAsTaskAsync_ThrowsException_WhenClientDoesNotSupportTaskAugmentedElicitation()
    {
        // Arrange - Client supports elicitation but NOT task-augmented elicitation
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities
        {
            Elicitation = new ElicitationCapability { Form = new() },
            // Note: No Tasks capability
        }, TestContext.Current.CancellationToken);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await server.ElicitAsTaskAsync(
                new ElicitRequestParams { Message = "test", RequestedSchema = new() },
                new McpTaskMetadata(),
                CancellationToken.None));

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task ElicitAsTaskAsync_SendsRequest_WhenClientSupportsTaskAugmentedElicitation()
    {
        // Arrange
        await using var transport = new TestServerTransport();

        // Configure transport to return a task result for elicitation
        transport.MockTask = new McpTask
        {
            TaskId = "elicit-task-456",
            Status = McpTaskStatus.Working,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
        };

        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities
        {
            Elicitation = new ElicitationCapability { Form = new() },
            Tasks = new McpTasksCapability
            {
                Requests = new RequestMcpTasksCapability
                {
                    Elicitation = new ElicitationMcpTasksCapability
                    {
                        Create = new CreateElicitationMcpTasksCapability()
                    }
                }
            }
        }, TestContext.Current.CancellationToken);

        // Act
        var task = await server.ElicitAsTaskAsync(
            new ElicitRequestParams { Message = "Please provide input", RequestedSchema = new() },
            new McpTaskMetadata { TimeToLive = TimeSpan.FromMinutes(10) },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(task);
        Assert.Equal("elicit-task-456", task.TaskId);
        Assert.Equal(McpTaskStatus.Working, task.Status);

        await transport.DisposeAsync();
        await runTask;
    }

    #endregion

    #region GetTaskAsync Tests

    [Fact]
    public async Task GetTaskAsync_ThrowsException_WhenClientDoesNotSupportTasks()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities(), TestContext.Current.CancellationToken);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await server.GetTaskAsync("task-id", CancellationToken.None));

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task GetTaskAsync_SendsRequest_AndReturnsTask()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        transport.MockTask = new McpTask
        {
            TaskId = "client-task-789",
            Status = McpTaskStatus.Completed,
            StatusMessage = "Task completed successfully",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            LastUpdatedAt = DateTimeOffset.UtcNow,
        };

        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities
        {
            Tasks = new McpTasksCapability()
        }, TestContext.Current.CancellationToken);

        // Act
        var task = await server.GetTaskAsync("client-task-789", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(task);
        Assert.Equal("client-task-789", task.TaskId);
        Assert.Equal(McpTaskStatus.Completed, task.Status);

        // Verify the request was sent
        var taskRequest = transport.SentMessages.OfType<JsonRpcRequest>()
            .FirstOrDefault(r => r.Method == RequestMethods.TasksGet);
        Assert.NotNull(taskRequest);

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task GetTaskAsync_ThrowsArgumentException_WhenTaskIdIsEmpty()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities
        {
            Tasks = new McpTasksCapability()
        }, TestContext.Current.CancellationToken);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await server.GetTaskAsync("", CancellationToken.None));

        await transport.DisposeAsync();
        await runTask;
    }

    #endregion

    #region GetTaskResultAsync Tests

    [Fact]
    public async Task GetTaskResultAsync_ThrowsException_WhenClientDoesNotSupportTasks()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities(), TestContext.Current.CancellationToken);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await server.GetTaskResultAsync<CreateMessageResult>("task-id", cancellationToken: CancellationToken.None));

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task GetTaskResultAsync_ReturnsDeserializedResult()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        transport.MockTaskResult = new CreateMessageResult
        {
            Content = [new TextContentBlock { Text = "Hello from task result!" }],
            Model = "gpt-4"
        };

        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities
        {
            Tasks = new McpTasksCapability()
        }, TestContext.Current.CancellationToken);

        // Act
        var result = await server.GetTaskResultAsync<CreateMessageResult>(
            "task-id", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("gpt-4", result.Model);
        Assert.Single(result.Content);
        var textContent = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Equal("Hello from task result!", textContent.Text);

        await transport.DisposeAsync();
        await runTask;
    }

    #endregion

    #region ListTasksAsync Tests

    [Fact]
    public async Task ListTasksAsync_ThrowsException_WhenClientDoesNotSupportTasks()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities(), TestContext.Current.CancellationToken);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await server.ListTasksAsync(CancellationToken.None));

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task ListTasksAsync_ThrowsException_WhenClientDoesNotSupportTaskListing()
    {
        // Arrange - Client supports tasks but NOT task listing
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities
        {
            Tasks = new McpTasksCapability
            {
                // Note: No List capability
            }
        }, TestContext.Current.CancellationToken);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await server.ListTasksAsync(CancellationToken.None));

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task ListTasksAsync_ReturnsTaskList()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        transport.MockTaskList =
        [
            new McpTask
            {
                TaskId = "task-a",
                Status = McpTaskStatus.Completed,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                LastUpdatedAt = DateTimeOffset.UtcNow,
            },
            new McpTask
            {
                TaskId = "task-b",
                Status = McpTaskStatus.Working,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                LastUpdatedAt = DateTimeOffset.UtcNow,
            },
            new McpTask
            {
                TaskId = "task-c",
                Status = McpTaskStatus.Failed,
                StatusMessage = "Task failed",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                LastUpdatedAt = DateTimeOffset.UtcNow,
            }
        ];

        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities
        {
            Tasks = new McpTasksCapability
            {
                List = new ListMcpTasksCapability()
            }
        }, TestContext.Current.CancellationToken);

        // Act
        var tasks = await server.ListTasksAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(tasks);
        Assert.Equal(3, tasks.Count);
        Assert.Equal("task-a", tasks[0].TaskId);
        Assert.Equal("task-b", tasks[1].TaskId);
        Assert.Equal("task-c", tasks[2].TaskId);

        await transport.DisposeAsync();
        await runTask;
    }

    #endregion

    #region CancelTaskAsync Tests

    [Fact]
    public async Task CancelTaskAsync_ThrowsException_WhenClientDoesNotSupportTasks()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities(), TestContext.Current.CancellationToken);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await server.CancelTaskAsync("task-id", CancellationToken.None));

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task CancelTaskAsync_ThrowsException_WhenClientDoesNotSupportTaskCancellation()
    {
        // Arrange - Client supports tasks but NOT task cancellation
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities
        {
            Tasks = new McpTasksCapability
            {
                // Note: No Cancel capability
            }
        }, TestContext.Current.CancellationToken);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await server.CancelTaskAsync("task-id", CancellationToken.None));

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task CancelTaskAsync_SendsRequest_AndReturnsCancelledTask()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        transport.MockTask = new McpTask
        {
            TaskId = "task-to-cancel",
            Status = McpTaskStatus.Working,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
            LastUpdatedAt = DateTimeOffset.UtcNow,
        };

        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities
        {
            Tasks = new McpTasksCapability
            {
                Cancel = new CancelMcpTasksCapability()
            }
        }, TestContext.Current.CancellationToken);

        // Act
        var task = await server.CancelTaskAsync("task-to-cancel", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(task);
        Assert.Equal("task-to-cancel", task.TaskId);
        Assert.Equal(McpTaskStatus.Cancelled, task.Status);

        // Verify the request was sent
        var cancelRequest = transport.SentMessages.OfType<JsonRpcRequest>()
            .FirstOrDefault(r => r.Method == RequestMethods.TasksCancel);
        Assert.NotNull(cancelRequest);

        await transport.DisposeAsync();
        await runTask;
    }

    #endregion

    #region PollTaskUntilCompleteAsync Tests

    [Fact]
    public async Task PollTaskUntilCompleteAsync_ReturnsImmediately_WhenTaskIsAlreadyComplete()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        transport.MockTask = new McpTask
        {
            TaskId = "completed-task",
            Status = McpTaskStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            LastUpdatedAt = DateTimeOffset.UtcNow,
        };

        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities
        {
            Tasks = new McpTasksCapability()
        }, TestContext.Current.CancellationToken);

        // Act
        var task = await server.PollTaskUntilCompleteAsync("completed-task", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(task);
        Assert.Equal("completed-task", task.TaskId);
        Assert.Equal(McpTaskStatus.Completed, task.Status);

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task PollTaskUntilCompleteAsync_ReturnsTask_WhenTaskFails()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        transport.MockTask = new McpTask
        {
            TaskId = "failed-task",
            Status = McpTaskStatus.Failed,
            StatusMessage = "Task execution failed",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            LastUpdatedAt = DateTimeOffset.UtcNow,
        };

        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities
        {
            Tasks = new McpTasksCapability()
        }, TestContext.Current.CancellationToken);

        // Act
        var task = await server.PollTaskUntilCompleteAsync("failed-task", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(task);
        Assert.Equal("failed-task", task.TaskId);
        Assert.Equal(McpTaskStatus.Failed, task.Status);
        Assert.Equal("Task execution failed", task.StatusMessage);

        await transport.DisposeAsync();
        await runTask;
    }

    #endregion

    #region WaitForTaskResultAsync Tests

    [Fact]
    public async Task WaitForTaskResultAsync_ReturnsTaskAndResult_WhenTaskCompletes()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        transport.MockTask = new McpTask
        {
            TaskId = "task-with-result",
            Status = McpTaskStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            LastUpdatedAt = DateTimeOffset.UtcNow,
        };
        transport.MockTaskResult = new CreateMessageResult
        {
            Content = [new TextContentBlock { Text = "Final result from task" }],
            Model = "test-model"
        };

        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities
        {
            Tasks = new McpTasksCapability()
        }, TestContext.Current.CancellationToken);

        // Act
        var (task, result) = await server.WaitForTaskResultAsync<CreateMessageResult>(
            "task-with-result", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(task);
        Assert.Equal("task-with-result", task.TaskId);
        Assert.Equal(McpTaskStatus.Completed, task.Status);

        Assert.NotNull(result);
        Assert.Equal("test-model", result.Model);
        var textContent = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal("Final result from task", textContent.Text);

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task WaitForTaskResultAsync_ThrowsException_WhenTaskFails()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        transport.MockTask = new McpTask
        {
            TaskId = "failed-task",
            Status = McpTaskStatus.Failed,
            StatusMessage = "Something went wrong",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            LastUpdatedAt = DateTimeOffset.UtcNow,
        };

        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities
        {
            Tasks = new McpTasksCapability()
        }, TestContext.Current.CancellationToken);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<McpException>(async () =>
            await server.WaitForTaskResultAsync<CreateMessageResult>(
                "failed-task", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("failed", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Something went wrong", ex.Message);

        await transport.DisposeAsync();
        await runTask;
    }

    [Fact]
    public async Task WaitForTaskResultAsync_ThrowsException_WhenTaskIsCancelled()
    {
        // Arrange
        await using var transport = new TestServerTransport();
        transport.MockTask = new McpTask
        {
            TaskId = "cancelled-task",
            Status = McpTaskStatus.Cancelled,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            LastUpdatedAt = DateTimeOffset.UtcNow,
        };

        await using var server = McpServer.Create(transport, _options, LoggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        await InitializeServerAsync(transport, new ClientCapabilities
        {
            Tasks = new McpTasksCapability()
        }, TestContext.Current.CancellationToken);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<McpException>(async () =>
            await server.WaitForTaskResultAsync<CreateMessageResult>(
                "cancelled-task", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("cancelled", ex.Message, StringComparison.OrdinalIgnoreCase);

        await transport.DisposeAsync();
        await runTask;
    }

    #endregion

    #region Helper Methods

    private static async Task InitializeServerAsync(TestServerTransport transport, ClientCapabilities capabilities, CancellationToken cancellationToken = default)
    {
        var initializeRequest = new JsonRpcRequest
        {
            Id = new RequestId("init-1"),
            Method = RequestMethods.Initialize,
            Params = JsonSerializer.SerializeToNode(new InitializeRequestParams
            {
                ProtocolVersion = "2024-11-05",
                Capabilities = capabilities,
                ClientInfo = new Implementation { Name = "test-client", Version = "1.0.0" }
            }, McpJsonUtilities.DefaultOptions)
        };

        var tcs = new TaskCompletionSource<bool>();
        transport.OnMessageSent = (message) =>
        {
            if (message is JsonRpcResponse response && response.Id == initializeRequest.Id)
            {
                tcs.TrySetResult(true);
            }
        };

        await transport.SendClientMessageAsync(initializeRequest, cancellationToken);

        // Wait for the initialize response to be sent
        await tcs.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);
    }

    #endregion
}
