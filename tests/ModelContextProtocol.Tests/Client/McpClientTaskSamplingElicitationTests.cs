using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Client;

/// <summary>
/// Integration tests for task-based sampling and elicitation on the client side.
/// Tests the client's ability to receive task-augmented requests from the server,
/// execute them as tasks, and report results.
/// </summary>
public class McpClientTaskSamplingElicitationTests : ClientServerTestBase
{
    public McpClientTaskSamplingElicitationTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        // Add task store for server-side task support  
        var taskStore = new InMemoryMcpTaskStore();
        services.AddSingleton<IMcpTaskStore>(taskStore);

        // Configure server to use the task store
        services.Configure<McpServerOptions>(options =>
        {
            options.TaskStore = taskStore;
        });

        // Add a tool that uses sampling to generate responses
        mcpServerBuilder.WithTools([McpServerTool.Create(
            async (string prompt, McpServer server, CancellationToken ct) =>
            {
                // This tool requests sampling from the client
                var result = await server.SampleAsync(new CreateMessageRequestParams
                {
                    Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = prompt }] }],
                    MaxTokens = 100
                }, ct);

                return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "No response";
            },
            new McpServerToolCreateOptions
            {
                Name = "sample-tool",
                Description = "A tool that uses sampling"
            }),
            McpServerTool.Create(
            async (string message, McpServer server, CancellationToken ct) =>
            {
                // This tool requests elicitation from the client
                var result = await server.ElicitAsync(new ElicitRequestParams
                {
                    Message = message,
                    RequestedSchema = new()
                }, ct);

                return result.Action == "confirm" ? "Confirmed" : "Declined";
            },
            new McpServerToolCreateOptions
            {
                Name = "elicit-tool",
                Description = "A tool that uses elicitation"
            })]);
    }

    private static IDictionary<string, JsonElement> CreateArguments(string key, object? value)
    {
        return new Dictionary<string, JsonElement>
        {
            [key] = JsonDocument.Parse($"\"{value}\"").RootElement.Clone()
        };
    }

    #region Client Task-Based Sampling Tests

    [Fact]
    public async Task Client_WithTaskStoreAndSamplingHandler_AdvertisesTaskAugmentedSamplingCapability()
    {
        // Arrange - Create client with task store and sampling handler
        var taskStore = new InMemoryMcpTaskStore();
        var clientOptions = new McpClientOptions
        {
            TaskStore = taskStore,
            Handlers = new McpClientHandlers
            {
                SamplingHandler = (request, progress, ct) =>
                {
                    return new ValueTask<CreateMessageResult>(new CreateMessageResult
                    {
                        Content = [new TextContentBlock { Text = "Sampled response" }],
                        Model = "test-model"
                    });
                }
            }
        };

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        // The server should see the client's task capabilities
        // We verify by checking server can use task-augmented requests
        Assert.NotNull(Server.ClientCapabilities);
        Assert.NotNull(Server.ClientCapabilities.Sampling);
        Assert.NotNull(Server.ClientCapabilities.Tasks);
        Assert.NotNull(Server.ClientCapabilities.Tasks.Requests?.Sampling?.CreateMessage);
    }

    [Fact]
    public async Task Client_WithoutTaskStore_DoesNotAdvertiseTaskAugmentedSamplingCapability()
    {
        // Arrange - Create client with sampling handler but NO task store
        var clientOptions = new McpClientOptions
        {
            // No TaskStore configured
            Handlers = new McpClientHandlers
            {
                SamplingHandler = (request, progress, ct) =>
                {
                    return new ValueTask<CreateMessageResult>(new CreateMessageResult
                    {
                        Content = [new TextContentBlock { Text = "Sampled response" }],
                        Model = "test-model"
                    });
                }
            }
        };

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        // The server should see sampling capability but NOT task-augmented sampling
        Assert.NotNull(Server.ClientCapabilities);
        Assert.NotNull(Server.ClientCapabilities.Sampling);

        // Task capabilities should be null (no task store)
        Assert.Null(Server.ClientCapabilities.Tasks);
    }

    [Fact]
    public async Task Server_SampleAsTaskAsync_FailsWhenClientDoesNotSupportTaskAugmentedSampling()
    {
        // Arrange - Client with sampling handler but NO task store
        var clientOptions = new McpClientOptions
        {
            Handlers = new McpClientHandlers
            {
                SamplingHandler = (request, progress, ct) =>
                {
                    return new ValueTask<CreateMessageResult>(new CreateMessageResult
                    {
                        Content = [new TextContentBlock { Text = "Response" }],
                        Model = "model"
                    });
                }
            }
        };

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        // Act & Assert - Server should throw when trying to use task-augmented sampling
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await Server.SampleAsTaskAsync(
                new CreateMessageRequestParams
                {
                    Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "Test" }] }],
                    MaxTokens = 100
                },
                new McpTaskMetadata(),
                TestContext.Current.CancellationToken);
        });

        Assert.Contains("task-augmented sampling", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Client_WithTaskStore_CanExecuteSamplingAsTask()
    {
        // Arrange
        var taskStore = new InMemoryMcpTaskStore();
        var samplingCompleted = new TaskCompletionSource<bool>();

        var clientOptions = new McpClientOptions
        {
            TaskStore = taskStore,
            Handlers = new McpClientHandlers
            {
                SamplingHandler = async (request, progress, ct) =>
                {
                    // Simulate some work
                    await Task.Delay(50, ct);
                    samplingCompleted.TrySetResult(true);
                    return new CreateMessageResult
                    {
                        Content = [new TextContentBlock { Text = "Task-based sampling response" }],
                        Model = "test-model"
                    };
                }
            }
        };

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        // Act - Server requests task-augmented sampling
        var mcpTask = await Server.SampleAsTaskAsync(
            new CreateMessageRequestParams
            {
                Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "Hello" }] }],
                MaxTokens = 100
            },
            new McpTaskMetadata(),
            TestContext.Current.CancellationToken);

        // Assert - Task was created
        Assert.NotNull(mcpTask);
        Assert.NotEmpty(mcpTask.TaskId);
        Assert.Equal(McpTaskStatus.Working, mcpTask.Status);

        // Wait for sampling to complete
        await samplingCompleted.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        // Poll until task is complete
        McpTask taskStatus;
        do
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            taskStatus = await Server.GetTaskAsync(mcpTask.TaskId, TestContext.Current.CancellationToken);
        }
        while (taskStatus.Status == McpTaskStatus.Working);

        Assert.Equal(McpTaskStatus.Completed, taskStatus.Status);

        // Get the result
        var result = await Server.GetTaskResultAsync<CreateMessageResult>(
            mcpTask.TaskId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        var textContent = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal("Task-based sampling response", textContent.Text);
    }

    #endregion

    #region Client Task-Based Elicitation Tests

    [Fact]
    public async Task Client_WithTaskStoreAndElicitationHandler_AdvertisesTaskAugmentedElicitationCapability()
    {
        // Arrange - Create client with task store and elicitation handler
        var taskStore = new InMemoryMcpTaskStore();
        var clientOptions = new McpClientOptions
        {
            TaskStore = taskStore,
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (request, ct) =>
                {
                    return new ValueTask<ElicitResult>(new ElicitResult { Action = "confirm" });
                }
            }
        };

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        // Verify client advertised task-augmented elicitation
        Assert.NotNull(Server.ClientCapabilities);
        Assert.NotNull(Server.ClientCapabilities.Elicitation);
        Assert.NotNull(Server.ClientCapabilities.Tasks);
        Assert.NotNull(Server.ClientCapabilities.Tasks.Requests?.Elicitation?.Create);
    }

    [Fact]
    public async Task Client_WithoutTaskStore_DoesNotAdvertiseTaskAugmentedElicitationCapability()
    {
        // Arrange - Create client with elicitation handler but NO task store
        var clientOptions = new McpClientOptions
        {
            // No TaskStore configured
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (request, ct) =>
                {
                    return new ValueTask<ElicitResult>(new ElicitResult { Action = "confirm" });
                }
            }
        };

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        // Verify elicitation is supported but NOT task-augmented
        Assert.NotNull(Server.ClientCapabilities);
        Assert.NotNull(Server.ClientCapabilities.Elicitation);
        Assert.Null(Server.ClientCapabilities.Tasks);
    }

    [Fact]
    public async Task Server_ElicitAsTaskAsync_FailsWhenClientDoesNotSupportTaskAugmentedElicitation()
    {
        // Arrange - Client with elicitation handler but NO task store
        var clientOptions = new McpClientOptions
        {
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (request, ct) =>
                {
                    return new ValueTask<ElicitResult>(new ElicitResult { Action = "confirm" });
                }
            }
        };

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        // Act & Assert - Server should throw when trying to use task-augmented elicitation
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await Server.ElicitAsTaskAsync(
                new ElicitRequestParams
                {
                    Message = "Please confirm",
                    RequestedSchema = new()
                },
                new McpTaskMetadata(),
                TestContext.Current.CancellationToken);
        });

        Assert.Contains("task-augmented elicitation", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Client_WithTaskStore_CanExecuteElicitationAsTask()
    {
        // Arrange
        var taskStore = new InMemoryMcpTaskStore();
        var elicitationCompleted = new TaskCompletionSource<bool>();

        var clientOptions = new McpClientOptions
        {
            TaskStore = taskStore,
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = async (request, ct) =>
                {
                    // Simulate user interaction time
                    await Task.Delay(50, ct);
                    elicitationCompleted.TrySetResult(true);
                    return new ElicitResult
                    {
                        Action = "accept",
                        Content = new Dictionary<string, JsonElement>
                        {
                            ["answer"] = JsonDocument.Parse("\"yes\"").RootElement.Clone()
                        }
                    };
                }
            }
        };

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        // Act - Server requests task-augmented elicitation
        var mcpTask = await Server.ElicitAsTaskAsync(
            new ElicitRequestParams
            {
                Message = "Do you want to proceed?",
                RequestedSchema = new()
            },
            new McpTaskMetadata(),
            TestContext.Current.CancellationToken);

        // Assert - Task was created
        Assert.NotNull(mcpTask);
        Assert.NotEmpty(mcpTask.TaskId);
        Assert.Equal(McpTaskStatus.Working, mcpTask.Status);

        // Wait for elicitation to complete
        await elicitationCompleted.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        // Poll until task is complete
        McpTask taskStatus;
        do
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            taskStatus = await Server.GetTaskAsync(mcpTask.TaskId, TestContext.Current.CancellationToken);
        }
        while (taskStatus.Status == McpTaskStatus.Working);

        Assert.Equal(McpTaskStatus.Completed, taskStatus.Status);

        // Get the result
        var result = await Server.GetTaskResultAsync<ElicitResult>(
            mcpTask.TaskId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("accept", result.Action);
    }

    #endregion

    #region Client Task Reporting Tests

    [Fact]
    public async Task Client_CanListOwnTasks()
    {
        // Arrange
        var taskStore = new InMemoryMcpTaskStore();
        var clientOptions = new McpClientOptions
        {
            TaskStore = taskStore,
            Handlers = new McpClientHandlers
            {
                SamplingHandler = async (request, progress, ct) =>
                {
                    await Task.Delay(50, ct);
                    return new CreateMessageResult
                    {
                        Content = [new TextContentBlock { Text = "Response" }],
                        Model = "model"
                    };
                }
            }
        };

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        // Create multiple tasks
        var task1 = await Server.SampleAsTaskAsync(
            new CreateMessageRequestParams { Messages = [], MaxTokens = 100 },
            new McpTaskMetadata(),
            TestContext.Current.CancellationToken);

        var task2 = await Server.SampleAsTaskAsync(
            new CreateMessageRequestParams { Messages = [], MaxTokens = 100 },
            new McpTaskMetadata(),
            TestContext.Current.CancellationToken);

        // Act - Server lists tasks from client
        var tasks = await Server.ListTasksAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(tasks);
        Assert.True(tasks.Count >= 2, "Should have at least 2 tasks");
        Assert.Contains(tasks, t => t.TaskId == task1.TaskId);
        Assert.Contains(tasks, t => t.TaskId == task2.TaskId);
    }

    [Fact]
    public async Task Client_CanCancelTasks()
    {
        // Arrange
        var taskStore = new InMemoryMcpTaskStore();
        var samplingStarted = new TaskCompletionSource<bool>();
        var allowCompletion = new TaskCompletionSource<bool>();

        var clientOptions = new McpClientOptions
        {
            TaskStore = taskStore,
            Handlers = new McpClientHandlers
            {
                SamplingHandler = async (request, progress, ct) =>
                {
                    samplingStarted.TrySetResult(true);
                    // Wait for either completion signal or cancellation
                    try
                    {
                        await allowCompletion.Task.WaitAsync(ct);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    return new CreateMessageResult
                    {
                        Content = [new TextContentBlock { Text = "Should not reach here" }],
                        Model = "model"
                    };
                }
            }
        };

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        // Create a task that will be in progress
        var mcpTask = await Server.SampleAsTaskAsync(
            new CreateMessageRequestParams { Messages = [], MaxTokens = 100 },
            new McpTaskMetadata(),
            TestContext.Current.CancellationToken);

        // Wait for sampling to start
        await samplingStarted.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        // Act - Cancel the task
        var cancelledTask = await Server.CancelTaskAsync(mcpTask.TaskId, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(cancelledTask);
        Assert.Equal(McpTaskStatus.Cancelled, cancelledTask.Status);

        // Allow completion to avoid hanging (the handler might still be running)
        allowCompletion.TrySetResult(true);
    }

    [Fact]
    public async Task Client_TaskStatusNotifications_SentWhenEnabled()
    {
        // Arrange
        var taskStore = new InMemoryMcpTaskStore();
        var workingNotificationReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedNotificationReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var notificationsReceived = new List<McpTask>();
        var notificationsLock = new object();
        string? expectedTaskId = null;
        var expectedTaskIdLock = new object();

        var clientOptions = new McpClientOptions
        {
            TaskStore = taskStore,
            SendTaskStatusNotifications = true,
            Handlers = new McpClientHandlers
            {
                SamplingHandler = async (request, progress, ct) =>
                {
                    await Task.Delay(100, ct);
                    return new CreateMessageResult
                    {
                        Content = [new TextContentBlock { Text = "Done" }],
                        Model = "model"
                    };
                }
            }
        };

        // Register notification handler on the server BEFORE creating the client
        var notificationHandler = Server.RegisterNotificationHandler(
            NotificationMethods.TaskStatusNotification,
            (notification, ct) =>
            {
                if (notification.Params is not { } paramsNode)
                {
                    return default;
                }

                var taskNotification = JsonSerializer.Deserialize<McpTaskStatusNotificationParams>(
                    paramsNode, McpJsonUtilities.DefaultOptions);
                if (taskNotification is null)
                {
                    return default;
                }

                // Only track notifications for our task
                string? taskId;
                lock (expectedTaskIdLock)
                {
                    taskId = expectedTaskId;
                }
                if (taskId is not null && taskNotification.TaskId != taskId)
                {
                    return default;
                }

                lock (notificationsLock)
                {
                    notificationsReceived.Add(new McpTask
                    {
                        TaskId = taskNotification.TaskId,
                        Status = taskNotification.Status,
                        CreatedAt = taskNotification.CreatedAt,
                        LastUpdatedAt = taskNotification.LastUpdatedAt
                    });
                }

                // Signal when we receive the Working and Completed notifications
                if (taskNotification.Status == McpTaskStatus.Working)
                {
                    workingNotificationReceived.TrySetResult(true);
                }
                else if (taskNotification.Status == McpTaskStatus.Completed)
                {
                    completedNotificationReceived.TrySetResult(true);
                }

                return default;
            });

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        // Act - Create a task
        var mcpTask = await Server.SampleAsTaskAsync(
            new CreateMessageRequestParams { Messages = [], MaxTokens = 100 },
            new McpTaskMetadata(),
            TestContext.Current.CancellationToken);

        // Store the expected task ID for filtering
        lock (expectedTaskIdLock)
        {
            expectedTaskId = mcpTask.TaskId;
        }

        // Wait for both Working and Completed notifications to arrive
        // The notifications are sent asynchronously so we need to wait for both
        await Task.WhenAll(
            workingNotificationReceived.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken),
            completedNotificationReceived.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken));

        // Assert - Should have received notifications for status transitions
        await notificationHandler.DisposeAsync();

        List<McpTask> notifications;
        lock (notificationsLock)
        {
            notifications = [.. notificationsReceived];
        }

        Assert.NotEmpty(notifications);
        Assert.Contains(notifications, t => t.Status == McpTaskStatus.Working);
        Assert.Contains(notifications, t => t.Status == McpTaskStatus.Completed);

        // Verify all notifications are for the correct task
        Assert.All(notifications, t => Assert.Equal(mcpTask.TaskId, t.TaskId));
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task Client_SamplingHandlerException_ResultsInFailedTask()
    {
        // Arrange
        var taskStore = new InMemoryMcpTaskStore();
        var samplingAttempted = new TaskCompletionSource<bool>();

        var clientOptions = new McpClientOptions
        {
            TaskStore = taskStore,
            Handlers = new McpClientHandlers
            {
                SamplingHandler = (request, progress, ct) =>
                {
                    samplingAttempted.TrySetResult(true);
                    throw new InvalidOperationException("Sampling failed!");
                }
            }
        };

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        // Act
        var mcpTask = await Server.SampleAsTaskAsync(
            new CreateMessageRequestParams { Messages = [], MaxTokens = 100 },
            new McpTaskMetadata(),
            TestContext.Current.CancellationToken);

        // Wait for sampling attempt
        await samplingAttempted.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        // Poll until task status changes
        McpTask taskStatus;
        do
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            taskStatus = await Server.GetTaskAsync(mcpTask.TaskId, TestContext.Current.CancellationToken);
        }
        while (taskStatus.Status == McpTaskStatus.Working);

        // Assert - Task should be in failed state
        Assert.Equal(McpTaskStatus.Failed, taskStatus.Status);
        Assert.NotNull(taskStatus.StatusMessage);
        Assert.Contains("Sampling failed!", taskStatus.StatusMessage);
    }

    [Fact]
    public async Task Client_ElicitationHandlerException_ResultsInFailedTask()
    {
        // Arrange
        var taskStore = new InMemoryMcpTaskStore();
        var elicitationAttempted = new TaskCompletionSource<bool>();

        var clientOptions = new McpClientOptions
        {
            TaskStore = taskStore,
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (request, ct) =>
                {
                    elicitationAttempted.TrySetResult(true);
                    throw new InvalidOperationException("Elicitation failed!");
                }
            }
        };

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        // Act
        var mcpTask = await Server.ElicitAsTaskAsync(
            new ElicitRequestParams
            {
                Message = "Test",
                RequestedSchema = new()
            },
            new McpTaskMetadata(),
            TestContext.Current.CancellationToken);

        // Wait for elicitation attempt
        await elicitationAttempted.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        // Poll until task status changes
        McpTask taskStatus;
        do
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            taskStatus = await Server.GetTaskAsync(mcpTask.TaskId, TestContext.Current.CancellationToken);
        }
        while (taskStatus.Status == McpTaskStatus.Working);

        // Assert
        Assert.Equal(McpTaskStatus.Failed, taskStatus.Status);
        Assert.NotNull(taskStatus.StatusMessage);
        Assert.Contains("Elicitation failed!", taskStatus.StatusMessage);
    }

    #endregion

    #region Capability Validation Tests

    [Fact]
    public async Task Client_WithOnlySamplingHandler_OnlyAdvertisesSamplingTasks()
    {
        // Arrange - Client with only sampling handler and task store
        var taskStore = new InMemoryMcpTaskStore();
        var clientOptions = new McpClientOptions
        {
            TaskStore = taskStore,
            Handlers = new McpClientHandlers
            {
                SamplingHandler = (request, progress, ct) =>
                {
                    return new ValueTask<CreateMessageResult>(new CreateMessageResult
                    {
                        Content = [new TextContentBlock { Text = "Response" }],
                        Model = "model"
                    });
                }
                // No ElicitationHandler
            }
        };

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        // Assert
        Assert.NotNull(Server.ClientCapabilities);
        Assert.NotNull(Server.ClientCapabilities.Tasks);

        // Should have sampling task capability
        Assert.NotNull(Server.ClientCapabilities.Tasks.Requests?.Sampling?.CreateMessage);

        // Should NOT have elicitation task capability
        Assert.Null(Server.ClientCapabilities.Tasks.Requests?.Elicitation);
    }

    [Fact]
    public async Task Client_WithOnlyElicitationHandler_OnlyAdvertisesElicitationTasks()
    {
        // Arrange - Client with only elicitation handler and task store
        var taskStore = new InMemoryMcpTaskStore();
        var clientOptions = new McpClientOptions
        {
            TaskStore = taskStore,
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (request, ct) =>
                {
                    return new ValueTask<ElicitResult>(new ElicitResult { Action = "confirm" });
                }
                // No SamplingHandler
            }
        };

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        // Assert
        Assert.NotNull(Server.ClientCapabilities);
        Assert.NotNull(Server.ClientCapabilities.Tasks);

        // Should have elicitation task capability
        Assert.NotNull(Server.ClientCapabilities.Tasks.Requests?.Elicitation?.Create);

        // Should NOT have sampling task capability
        Assert.Null(Server.ClientCapabilities.Tasks.Requests?.Sampling);
    }

    [Fact]
    public async Task Client_WithBothHandlers_AdvertisesBothTaskCapabilities()
    {
        // Arrange - Client with both handlers and task store
        var taskStore = new InMemoryMcpTaskStore();
        var clientOptions = new McpClientOptions
        {
            TaskStore = taskStore,
            Handlers = new McpClientHandlers
            {
                SamplingHandler = (request, progress, ct) =>
                {
                    return new ValueTask<CreateMessageResult>(new CreateMessageResult
                    {
                        Content = [new TextContentBlock { Text = "Response" }],
                        Model = "model"
                    });
                },
                ElicitationHandler = (request, ct) =>
                {
                    return new ValueTask<ElicitResult>(new ElicitResult { Action = "confirm" });
                }
            }
        };

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        // Assert
        Assert.NotNull(Server.ClientCapabilities);
        Assert.NotNull(Server.ClientCapabilities.Tasks);
        Assert.NotNull(Server.ClientCapabilities.Tasks.Requests);

        // Should have both capabilities
        Assert.NotNull(Server.ClientCapabilities.Tasks.Requests.Sampling?.CreateMessage);
        Assert.NotNull(Server.ClientCapabilities.Tasks.Requests.Elicitation?.Create);

        // Should also have list and cancel capabilities
        Assert.NotNull(Server.ClientCapabilities.Tasks.List);
        Assert.NotNull(Server.ClientCapabilities.Tasks.Cancel);
    }

    [Fact]
    public async Task Client_WithNoHandlers_DoesNotAdvertiseTaskCapabilities()
    {
        // Arrange - Client with task store but no handlers
        var taskStore = new InMemoryMcpTaskStore();
        var clientOptions = new McpClientOptions
        {
            TaskStore = taskStore,
            Handlers = new McpClientHandlers()
            // No handlers configured
        };

        await using McpClient client = await CreateMcpClientForServer(clientOptions);

        // Assert - No capabilities should be advertised without handlers
        Assert.NotNull(Server.ClientCapabilities);

        // Note: Tasks capability is advertised based on task store being present,
        // but request types depend on specific handlers
        if (Server.ClientCapabilities.Tasks is not null)
        {
            // If Tasks is present, requests should be null or have no request types
            var requests = Server.ClientCapabilities.Tasks.Requests;
            if (requests is not null)
            {
                Assert.Null(requests.Sampling);
                Assert.Null(requests.Elicitation);
            }
        }
    }

    #endregion
}
