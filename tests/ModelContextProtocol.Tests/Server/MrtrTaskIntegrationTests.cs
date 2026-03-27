using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for the interaction between MRTR and the Tasks feature — verifying that MRTR-driven
/// tool calls correctly track task status (InputRequired), and that task-based sampling
/// bypasses MRTR interception.
/// </summary>
public class MrtrTaskIntegrationTests : ClientServerTestBase
{
    public MrtrTaskIntegrationTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        var taskStore = new InMemoryMcpTaskStore();
        services.AddSingleton<IMcpTaskStore>(taskStore);
        services.Configure<McpServerOptions>(options =>
        {
            options.TaskStore = taskStore;
            options.ExperimentalProtocolVersion = "2026-06-XX";
        });

        mcpServerBuilder.WithTools([
            McpServerTool.Create(
                async (string prompt, McpServer server, CancellationToken ct) =>
                {
                    // This tool calls SampleAsync which goes through MRTR when the client supports it.
                    // When running in a task context, SendRequestWithTaskStatusTrackingAsync should
                    // set task status to InputRequired while awaiting the sampling result.
                    var result = await server.SampleAsync(new CreateMessageRequestParams
                    {
                        Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = prompt }] }],
                        MaxTokens = 100
                    }, ct);

                    return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "No response";
                },
                new McpServerToolCreateOptions
                {
                    Name = "sampling-tool",
                    Description = "A tool that requests sampling from the client"
                }),
            McpServerTool.Create(
                async (string message, McpServer server, CancellationToken ct) =>
                {
                    var result = await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = message,
                        RequestedSchema = new()
                    }, ct);

                    return $"{result.Action}";
                },
                new McpServerToolCreateOptions
                {
                    Name = "elicitation-tool",
                    Description = "A tool that requests elicitation from the client"
                }),
        ]);
    }

    [Fact]
    public async Task TaskAugmentedToolCall_WithMrtrSampling_TracksInputRequiredStatus()
    {
        StartServer();
        var taskStore = new InMemoryMcpTaskStore();
        var samplingStarted = new TaskCompletionSource<bool>();
        var samplingCanProceed = new TaskCompletionSource<bool>();

        var clientOptions = new McpClientOptions
        {
            ExperimentalProtocolVersion = "2026-06-XX",
            TaskStore = taskStore,
            Handlers = new McpClientHandlers
            {
                SamplingHandler = async (request, progress, ct) =>
                {
                    samplingStarted.TrySetResult(true);
                    // Wait until test signals to proceed — this gives us time to check task status
                    await samplingCanProceed.Task.WaitAsync(ct);
                    return new CreateMessageResult
                    {
                        Content = [new TextContentBlock { Text = "Sampled response" }],
                        Model = "test-model"
                    };
                }
            }
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        // Start task-augmented tool call
        var mcpTask = await Server.SampleAsTaskAsync(
            new CreateMessageRequestParams
            {
                Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "Test" }] }],
                MaxTokens = 100
            },
            new McpTaskMetadata(),
            TestContext.Current.CancellationToken);

        Assert.NotNull(mcpTask);
        Assert.Equal(McpTaskStatus.Working, mcpTask.Status);

        // Wait for sampling handler to be called — this means MRTR resolved the input request
        await samplingStarted.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        // Let the sampling handler complete
        samplingCanProceed.TrySetResult(true);

        // Poll until task completes
        McpTask taskStatus;
        do
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            taskStatus = await Server.GetTaskAsync(mcpTask.TaskId, TestContext.Current.CancellationToken);
        }
        while (taskStatus.Status == McpTaskStatus.Working || taskStatus.Status == McpTaskStatus.InputRequired);

        Assert.Equal(McpTaskStatus.Completed, taskStatus.Status);

        // Verify the result is correct
        var result = await Server.GetTaskResultAsync<CreateMessageResult>(
            mcpTask.TaskId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        var textContent = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal("Sampled response", textContent.Text);
    }

    [Fact]
    public async Task TaskAugmentedToolCall_WithMrtrElicitation_CompletesSuccessfully()
    {
        StartServer();
        var clientOptions = new McpClientOptions
        {
            ExperimentalProtocolVersion = "2026-06-XX",
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (request, ct) =>
                {
                    return new ValueTask<ElicitResult>(new ElicitResult { Action = "confirm" });
                }
            }
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        // Call the elicitation tool — MRTR resolves the elicitation request via the client handler
        var result = await client.CallToolAsync("elicitation-tool",
            new Dictionary<string, object?> { ["message"] = "Do you agree?" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("confirm", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task SampleAsTaskAsync_BypassesMrtrInterception()
    {
        // SampleAsTaskAsync sends a request with "task" metadata in the params.
        // Even when MRTR context is active, these requests should go over the wire
        // (they expect CreateTaskResult, not CreateMessageResult).
        StartServer();
        var taskStore = new InMemoryMcpTaskStore();

        var clientOptions = new McpClientOptions
        {
            ExperimentalProtocolVersion = "2026-06-XX",
            TaskStore = taskStore,
            Handlers = new McpClientHandlers
            {
                SamplingHandler = async (request, progress, ct) =>
                {
                    await Task.Delay(50, ct);
                    return new CreateMessageResult
                    {
                        Content = [new TextContentBlock { Text = "Task-based response" }],
                        Model = "test-model"
                    };
                }
            }
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        // SampleAsTaskAsync should work normally — it sends over the wire, not through MRTR.
        var mcpTask = await Server.SampleAsTaskAsync(
            new CreateMessageRequestParams
            {
                Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "Hello" }] }],
                MaxTokens = 100
            },
            new McpTaskMetadata(),
            TestContext.Current.CancellationToken);

        Assert.NotNull(mcpTask);
        Assert.NotEmpty(mcpTask.TaskId);
        Assert.Equal(McpTaskStatus.Working, mcpTask.Status);

        // Poll until task completes
        McpTask taskStatus;
        do
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            taskStatus = await Server.GetTaskAsync(mcpTask.TaskId, TestContext.Current.CancellationToken);
        }
        while (taskStatus.Status == McpTaskStatus.Working);

        Assert.Equal(McpTaskStatus.Completed, taskStatus.Status);

        // Retrieve and verify the result
        var result = await Server.GetTaskResultAsync<CreateMessageResult>(
            mcpTask.TaskId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        var textContent = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal("Task-based response", textContent.Text);
    }

    [Fact]
    public async Task MrtrToolCall_ThenTaskBasedSampling_BothWorkCorrectly()
    {
        // Verify that MRTR tool calls and task-based sampling can coexist in the same session.
        StartServer();
        var taskStore = new InMemoryMcpTaskStore();

        var clientOptions = new McpClientOptions
        {
            ExperimentalProtocolVersion = "2026-06-XX",
            TaskStore = taskStore,
            Handlers = new McpClientHandlers
            {
                SamplingHandler = (request, progress, ct) =>
                {
                    var text = request?.Messages[^1].Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
                    return new ValueTask<CreateMessageResult>(new CreateMessageResult
                    {
                        Content = [new TextContentBlock { Text = $"Response: {text}" }],
                        Model = "test-model"
                    });
                }
            }
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        // First: MRTR tool call (synchronous sampling inside a tool)
        var mrtrResult = await client.CallToolAsync("sampling-tool",
            new Dictionary<string, object?> { ["prompt"] = "MRTR test" },
            cancellationToken: TestContext.Current.CancellationToken);

        var mrtrContent = Assert.Single(mrtrResult.Content);
        Assert.Equal("Response: MRTR test", Assert.IsType<TextContentBlock>(mrtrContent).Text);

        // Second: Task-based sampling (goes over the wire, bypasses MRTR)
        var mcpTask = await Server.SampleAsTaskAsync(
            new CreateMessageRequestParams
            {
                Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "Task test" }] }],
                MaxTokens = 100
            },
            new McpTaskMetadata(),
            TestContext.Current.CancellationToken);

        Assert.NotNull(mcpTask);

        // Poll until task completes
        McpTask taskStatus;
        do
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            taskStatus = await Server.GetTaskAsync(mcpTask.TaskId, TestContext.Current.CancellationToken);
        }
        while (taskStatus.Status == McpTaskStatus.Working);

        Assert.Equal(McpTaskStatus.Completed, taskStatus.Status);

        var taskResult = await Server.GetTaskResultAsync<CreateMessageResult>(
            mcpTask.TaskId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(taskResult);
        var taskContent = Assert.IsType<TextContentBlock>(Assert.Single(taskResult.Content));
        Assert.Equal("Response: Task test", taskContent.Text);
    }
}
