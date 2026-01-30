using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for automatic InputRequired status tracking when server-to-client
/// requests (SampleAsync, ElicitAsync) are made during task-augmented tool execution.
/// </summary>
public class AutomaticInputRequiredStatusTests : LoggedTest
{
    public AutomaticInputRequiredStatusTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

#pragma warning disable MCPEXP001 // Tasks feature is experimental

    [Fact]
    public async Task TaskStatus_TransitionsToInputRequired_DuringSampleAsync()
    {
        // Arrange
        var taskStore = new InMemoryMcpTaskStore();
        var statusesDuringSampling = new List<McpTaskStatus>();
        var samplingRequestReceived = new TaskCompletionSource<bool>();
        var continueSampling = new TaskCompletionSource<bool>();

        await using var fixture = new InputRequiredTestFixture(
            LoggerFactory,
            configureServer: (services, builder) =>
            {
                services.AddSingleton<IMcpTaskStore>(taskStore);
                services.Configure<McpServerOptions>(options =>
                {
                    options.TaskStore = taskStore;
                    options.SendTaskStatusNotifications = true; // Enable notifications
                });

                // Tool that calls SampleAsync during execution
                builder.WithTools([McpServerTool.Create(
                    async (string prompt, McpServer server, CancellationToken ct) =>
                    {
                        // Call SampleAsync - this should trigger InputRequired status
                        var result = await server.SampleAsync(new CreateMessageRequestParams
                        {
                            Messages = [new SamplingMessage 
                            { 
                                Role = Role.User, 
                                Content = [new TextContentBlock { Text = prompt }] 
                            }],
                            MaxTokens = 100
                        }, ct);

                        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
                        return textContent?.Text ?? "No response";
                    },
                    new McpServerToolCreateOptions
                    {
                        Name = "sampling-tool",
                        Description = "A tool that uses sampling"
                    })]);
            },
            configureClient: clientOptions =>
            {
                clientOptions.Handlers = new McpClientHandlers
                {
                    SamplingHandler = async (request, progress, ct) =>
                    {
                        // Signal that we received the sampling request
                        samplingRequestReceived.TrySetResult(true);

                        // Wait for permission to continue (so we can check status)
                        await continueSampling.Task.WaitAsync(ct);

                        return new CreateMessageResult
                        {
                            Content = [new TextContentBlock { Text = "Sampled response" }],
                            Model = "test-model"
                        };
                    }
                };
            });

        // Act - Call the tool as a task
        var mcpTask = await fixture.Client.CallToolAsTaskAsync(
            "sampling-tool",
            arguments: new Dictionary<string, object?> { ["prompt"] = "Hello" },
            taskMetadata: new McpTaskMetadata(),
            progress: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Wait for the sampling request to be received by the client
        await samplingRequestReceived.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        // Check the task status while sampling is in progress
        var statusDuringSampling = await taskStore.GetTaskAsync(
            mcpTask.TaskId, 
            cancellationToken: TestContext.Current.CancellationToken);

        if (statusDuringSampling is not null)
        {
            statusesDuringSampling.Add(statusDuringSampling.Status);
        }

        // Allow sampling to complete
        continueSampling.TrySetResult(true);

        // Wait for task to complete
        McpTask? finalStatus = null;
        int maxAttempts = 50;
        do
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            finalStatus = await taskStore.GetTaskAsync(mcpTask.TaskId, cancellationToken: TestContext.Current.CancellationToken);
            maxAttempts--;
        }
        while (finalStatus?.Status is not McpTaskStatus.Completed && maxAttempts > 0);

        // Assert - Status should have been InputRequired during sampling
        Assert.Contains(McpTaskStatus.InputRequired, statusesDuringSampling);
        
        // Final status should be Completed
        Assert.NotNull(finalStatus);
        Assert.Equal(McpTaskStatus.Completed, finalStatus.Status);
    }

    [Fact]
    public async Task TaskStatus_TransitionsToInputRequired_DuringElicitAsync()
    {
        // Arrange
        var taskStore = new InMemoryMcpTaskStore();
        var statusesDuringElicitation = new List<McpTaskStatus>();
        var elicitationRequestReceived = new TaskCompletionSource<bool>();
        var continueElicitation = new TaskCompletionSource<bool>();

        await using var fixture = new InputRequiredTestFixture(
            LoggerFactory,
            configureServer: (services, builder) =>
            {
                services.AddSingleton<IMcpTaskStore>(taskStore);
                services.Configure<McpServerOptions>(options =>
                {
                    options.TaskStore = taskStore;
                    options.SendTaskStatusNotifications = true;
                });

                // Tool that calls ElicitAsync during execution
                builder.WithTools([McpServerTool.Create(
                    async (string message, McpServer server, CancellationToken ct) =>
                    {
                        // Call ElicitAsync - this should trigger InputRequired status
                        var result = await server.ElicitAsync(new ElicitRequestParams
                        {
                            Message = message,
                            RequestedSchema = new()
                        }, ct);

                        return result.Action == "confirm" ? "Confirmed" : "Declined";
                    },
                    new McpServerToolCreateOptions
                    {
                        Name = "elicitation-tool",
                        Description = "A tool that uses elicitation"
                    })]);
            },
            configureClient: clientOptions =>
            {
                clientOptions.Handlers = new McpClientHandlers
                {
                    ElicitationHandler = async (request, ct) =>
                    {
                        // Signal that we received the elicitation request
                        elicitationRequestReceived.TrySetResult(true);

                        // Wait for permission to continue
                        await continueElicitation.Task.WaitAsync(ct);

                        return new ElicitResult { Action = "confirm" };
                    }
                };
            });

        // Act - Call the tool as a task
        var mcpTask = await fixture.Client.CallToolAsTaskAsync(
            "elicitation-tool",
            arguments: new Dictionary<string, object?> { ["message"] = "Please confirm" },
            taskMetadata: new McpTaskMetadata(),
            progress: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Wait for the elicitation request to be received
        await elicitationRequestReceived.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        // Check the task status while elicitation is in progress
        var statusDuringElicitation = await taskStore.GetTaskAsync(
            mcpTask.TaskId,
            cancellationToken: TestContext.Current.CancellationToken);

        if (statusDuringElicitation is not null)
        {
            statusesDuringElicitation.Add(statusDuringElicitation.Status);
        }

        // Allow elicitation to complete
        continueElicitation.TrySetResult(true);

        // Wait for task to complete
        McpTask? finalStatus = null;
        int maxAttempts = 50;
        do
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            finalStatus = await taskStore.GetTaskAsync(mcpTask.TaskId, cancellationToken: TestContext.Current.CancellationToken);
            maxAttempts--;
        }
        while (finalStatus?.Status is not McpTaskStatus.Completed && maxAttempts > 0);

        // Assert - Status should have been InputRequired during elicitation
        Assert.Contains(McpTaskStatus.InputRequired, statusesDuringElicitation);

        // Final status should be Completed
        Assert.NotNull(finalStatus);
        Assert.Equal(McpTaskStatus.Completed, finalStatus.Status);
    }

    [Fact]
    public async Task TaskStatus_ReturnsToWorking_AfterSamplingCompletes()
    {
        // Arrange
        var taskStore = new InMemoryMcpTaskStore();
        var samplingCompleted = new TaskCompletionSource<bool>();
        var checkStatusAfterSampling = new TaskCompletionSource<bool>();

        await using var fixture = new InputRequiredTestFixture(
            LoggerFactory,
            configureServer: (services, builder) =>
            {
                services.AddSingleton<IMcpTaskStore>(taskStore);
                services.Configure<McpServerOptions>(options =>
                {
                    options.TaskStore = taskStore;
                });

                // Tool that calls SampleAsync and then waits
                builder.WithTools([McpServerTool.Create(
                    async (string prompt, McpServer server, CancellationToken ct) =>
                    {
                        // Call SampleAsync
                        var result = await server.SampleAsync(new CreateMessageRequestParams
                        {
                            Messages = [new SamplingMessage 
                            { 
                                Role = Role.User, 
                                Content = [new TextContentBlock { Text = prompt }] 
                            }],
                            MaxTokens = 100
                        }, ct);

                        // Signal that sampling completed
                        samplingCompleted.TrySetResult(true);

                        // Wait so test can check status
                        await checkStatusAfterSampling.Task.WaitAsync(ct);

                        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
                        return textContent?.Text ?? "No response";
                    },
                    new McpServerToolCreateOptions
                    {
                        Name = "sampling-tool",
                        Description = "A tool that uses sampling"
                    })]);
            },
            configureClient: clientOptions =>
            {
                clientOptions.Handlers = new McpClientHandlers
                {
                    SamplingHandler = (request, progress, ct) =>
                    {
                        // Return immediately to let sampling complete
                        return new ValueTask<CreateMessageResult>(new CreateMessageResult
                        {
                            Content = [new TextContentBlock { Text = "Response" }],
                            Model = "test-model"
                        });
                    }
                };
            });

        // Act - Call the tool as a task
        var mcpTask = await fixture.Client.CallToolAsTaskAsync(
            "sampling-tool",
            arguments: new Dictionary<string, object?> { ["prompt"] = "Hello" },
            taskMetadata: new McpTaskMetadata(),
            progress: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // Wait for sampling to complete inside the tool
        await samplingCompleted.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        // Small delay to ensure status update is processed
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Check status after sampling completed (should be back to Working)
        var taskAfterSampling = await taskStore.GetTaskAsync(mcpTask.TaskId, cancellationToken: TestContext.Current.CancellationToken);

        // Allow tool to complete
        checkStatusAfterSampling.TrySetResult(true);

        // Assert - Status should be Working after sampling completes (before tool completes)
        Assert.NotNull(taskAfterSampling);
        Assert.Equal(McpTaskStatus.Working, taskAfterSampling.Status);
    }

    [Fact]
    public async Task TaskStatus_DoesNotChangeToInputRequired_ForNonTaskExecution()
    {
        // Arrange - When a tool is NOT executed as a task, SampleAsync should not change any task status
        var taskStore = new InMemoryMcpTaskStore();
        var samplingCompleted = new TaskCompletionSource<bool>();

        await using var fixture = new InputRequiredTestFixture(
            LoggerFactory,
            configureServer: (services, builder) =>
            {
                services.AddSingleton<IMcpTaskStore>(taskStore);
                services.Configure<McpServerOptions>(options =>
                {
                    options.TaskStore = taskStore;
                });

                // Tool that calls SampleAsync - note it doesn't have TaskSupport.Required so can be called directly
                builder.WithTools([McpServerTool.Create(
                    async (string prompt, McpServer server, CancellationToken ct) =>
                    {
                        var result = await server.SampleAsync(new CreateMessageRequestParams
                        {
                            Messages = [new SamplingMessage 
                            { 
                                Role = Role.User, 
                                Content = [new TextContentBlock { Text = prompt }] 
                            }],
                            MaxTokens = 100
                        }, ct);

                        samplingCompleted.TrySetResult(true);
                        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
                        return textContent?.Text ?? "No response";
                    },
                    new McpServerToolCreateOptions
                    {
                        Name = "sampling-tool",
                        Description = "A tool that uses sampling",
                        Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Optional }
                    })]);
            },
            configureClient: clientOptions =>
            {
                clientOptions.Handlers = new McpClientHandlers
                {
                    SamplingHandler = (request, progress, ct) =>
                    {
                        return new ValueTask<CreateMessageResult>(new CreateMessageResult
                        {
                            Content = [new TextContentBlock { Text = "Response" }],
                            Model = "test-model"
                        });
                    }
                };
            });

        // Act - Call the tool DIRECTLY (not as a task)
        var result = await fixture.Client.CallToolAsync(
            "sampling-tool",
            arguments: new Dictionary<string, object?> { ["prompt"] = "Hello" },
            cancellationToken: TestContext.Current.CancellationToken);

        await samplingCompleted.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        // Assert - No task should exist (tool was not called as a task)
        var tasks = await taskStore.ListTasksAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(tasks.Tasks);
        
        // And the result should still work
        Assert.NotNull(result);
    }

#pragma warning restore MCPEXP001

    /// <summary>
    /// Test fixture that supports both server and client configuration for InputRequired status tests.
    /// </summary>
    private sealed class InputRequiredTestFixture : IAsyncDisposable
    {
        private readonly Pipe _clientToServerPipe = new();
        private readonly Pipe _serverToClientPipe = new();
        private readonly IServiceProvider _serviceProvider;
        private readonly McpServer _server;
        private readonly Task _serverTask;
        private readonly CancellationTokenSource _cts;

        public McpClient Client { get; }
        public McpServer Server => _server;

        public InputRequiredTestFixture(
            ILoggerFactory loggerFactory,
            Action<ServiceCollection, IMcpServerBuilder>? configureServer = null,
            Action<McpClientOptions>? configureClient = null)
        {
            _cts = new CancellationTokenSource();

            // Configure server
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

            // Configure client
            var clientOptions = new McpClientOptions();
            configureClient?.Invoke(clientOptions);

            // Create client synchronously (test code)
            Client = McpClient.CreateAsync(
                new StreamClientTransport(
                    serverInput: _clientToServerPipe.Writer.AsStream(),
                    _serverToClientPipe.Reader.AsStream(),
                    loggerFactory),
                clientOptions: clientOptions,
                loggerFactory: loggerFactory,
                cancellationToken: TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
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
