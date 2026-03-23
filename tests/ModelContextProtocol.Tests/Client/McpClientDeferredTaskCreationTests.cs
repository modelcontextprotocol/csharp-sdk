using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Client;

/// <summary>
/// Tests for deferred task creation, where a tool performs ephemeral MRTR exchanges
/// before committing to a background task via <see cref="McpServer.CreateTaskAsync(CancellationToken)"/>.
/// </summary>
public class McpClientDeferredTaskCreationTests : ClientServerTestBase
{
    private readonly TaskCompletionSource<bool> _toolAfterTaskCreation = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly InMemoryMcpTaskStore _taskStore = new();

    public McpClientDeferredTaskCreationTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.AddSingleton<IMcpTaskStore>(_taskStore);
        services.Configure<McpServerOptions>(options =>
        {
            options.TaskStore = _taskStore;
            options.ExperimentalProtocolVersion = "2026-06-XX";
        });

        mcpServerBuilder.WithTools([
            // Tool that elicits before creating a task, then does work in background.
            McpServerTool.Create(
                async (string vmName, McpServer server, CancellationToken ct) =>
                {
                    // Phase 1: Ephemeral MRTR — confirm with user before starting expensive work.
                    var confirmation = await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = $"Provision VM '{vmName}'? This will incur costs.",
                        RequestedSchema = new()
                    }, ct);

                    if (confirmation.Action != "confirm")
                    {
                        return "Cancelled by user.";
                    }

                    // Phase 2: Transition to task.
                    await server.CreateTaskAsync(ct);
                    _toolAfterTaskCreation.TrySetResult(true);

                    // Phase 3: Background work (simulated).
                    await Task.Delay(50, ct);
                    return $"VM '{vmName}' provisioned successfully.";
                },
                new McpServerToolCreateOptions
                {
                    Name = "provision-vm",
                    Description = "Provisions a VM with user confirmation",
                    DeferTaskCreation = true,
                    Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Optional },
                }),

            // Tool that does MRTR but returns without creating a task.
            McpServerTool.Create(
                async (string question, McpServer server, CancellationToken ct) =>
                {
                    var result = await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = question,
                        RequestedSchema = new()
                    }, ct);

                    return $"Answer: {result.Action}";
                },
                new McpServerToolCreateOptions
                {
                    Name = "ask-question",
                    Description = "Asks a question and returns the answer without creating a task",
                    DeferTaskCreation = true,
                    Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Optional },
                }),

            // Tool that does NOT have DeferTaskCreation — existing behavior.
            McpServerTool.Create(
                async (string input, CancellationToken ct) =>
                {
                    await Task.Delay(50, ct);
                    return $"Processed: {input}";
                },
                new McpServerToolCreateOptions
                {
                    Name = "immediate-task-tool",
                    Description = "A task tool with immediate task creation (default)",
                    Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Optional },
                }),

            // Tool that does multiple MRTR rounds, then creates a task.
            McpServerTool.Create(
                async (McpServer server, CancellationToken ct) =>
                {
                    // Round 1: Ask for name.
                    var nameResult = await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = "What is your name?",
                        RequestedSchema = new()
                    }, ct);

                    // Round 2: Ask for email.
                    var emailResult = await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = "What is your email?",
                        RequestedSchema = new()
                    }, ct);

                    // Transition to task after gathering all input.
                    await server.CreateTaskAsync(ct);

                    await Task.Delay(50, ct);
                    return $"Registered: {nameResult.Action}, {emailResult.Action}";
                },
                new McpServerToolCreateOptions
                {
                    Name = "multi-round-then-task",
                    Description = "Does multiple MRTR rounds then creates a task",
                    DeferTaskCreation = true,
                    Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Optional },
                }),
        ]);
    }

    private static McpClientHandlers CreateElicitationHandlers()
    {
        return new McpClientHandlers
        {
            ElicitationHandler = (request, ct) => new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "confirm",
                Content = new Dictionary<string, JsonElement>()
            })
        };
    }

    private async Task<CallToolResult> CallToolWithTaskMetadataAsync(
        McpClient client, string toolName, Dictionary<string, object?>? arguments = null)
    {
        var requestParams = new CallToolRequestParams
        {
            Name = toolName,
            Task = new McpTaskMetadata(),
        };

        if (arguments is not null)
        {
            requestParams.Arguments = arguments.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value is not null
                    ? JsonSerializer.SerializeToElement(kvp.Value, McpJsonUtilities.DefaultOptions)
                    : default);
        }

        return await client.CallToolAsync(requestParams, TestContext.Current.CancellationToken);
    }

    private McpClientOptions CreateClientOptions(McpClientHandlers? handlers = null)
    {
        return new McpClientOptions
        {
            ExperimentalProtocolVersion = "2026-06-XX",
            TaskStore = _taskStore,
            Handlers = handlers ?? CreateElicitationHandlers()
        };
    }

    private async Task<McpTask> WaitForTaskCompletionAsync(string taskId)
    {
        McpTask? taskStatus;
        do
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            taskStatus = await _taskStore.GetTaskAsync(taskId, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(taskStatus);
        }
        while (taskStatus.Status is McpTaskStatus.Working or McpTaskStatus.InputRequired);

        return taskStatus;
    }

    [Fact]
    public async Task DeferredTaskCreation_ElicitThenCreateTask_ReturnsTaskResult()
    {
        StartServer();
        await using var client = await CreateMcpClientForServer(CreateClientOptions());

        var result = await CallToolWithTaskMetadataAsync(client, "provision-vm",
            new Dictionary<string, object?> { ["vmName"] = "test-vm" });

        // The result should have a task (created after MRTR elicitation).
        Assert.NotNull(result.Task);
        Assert.NotEmpty(result.Task.TaskId);

        // Wait for the tool to finish in the background.
        await _toolAfterTaskCreation.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var taskStatus = await WaitForTaskCompletionAsync(result.Task.TaskId);
        Assert.Equal(McpTaskStatus.Completed, taskStatus.Status);
    }

    [Fact]
    public async Task DeferredTaskCreation_ElicitWithoutCreatingTask_ReturnsNormalResult()
    {
        StartServer();
        await using var client = await CreateMcpClientForServer(CreateClientOptions());

        var result = await CallToolWithTaskMetadataAsync(client, "ask-question",
            new Dictionary<string, object?> { ["question"] = "How are you?" });

        // Tool returned without calling CreateTaskAsync — normal result, no task.
        Assert.Null(result.Task);
        var content = Assert.Single(result.Content);
        Assert.Equal("Answer: confirm", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task DeferredTaskCreation_WithoutTaskMetadata_NormalExecution()
    {
        StartServer();
        await using var client = await CreateMcpClientForServer(CreateClientOptions());

        // Call without task metadata — the tool does MRTR normally, no task involved.
        var result = await client.CallToolAsync("ask-question",
            new Dictionary<string, object?> { ["question"] = "No task" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result.Task);
        Assert.Equal("Answer: confirm", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task DeferredTaskCreation_MultipleRoundsThenCreateTask_AllRoundsComplete()
    {
        StartServer();
        var elicitCount = 0;
        var handlers = new McpClientHandlers
        {
            ElicitationHandler = (request, ct) =>
            {
                var count = Interlocked.Increment(ref elicitCount);
                var value = count == 1 ? "Alice" : "alice@example.com";
                return new ValueTask<ElicitResult>(new ElicitResult
                {
                    Action = value,
                    Content = new Dictionary<string, JsonElement>()
                });
            }
        };

        await using var client = await CreateMcpClientForServer(CreateClientOptions(handlers));

        var result = await CallToolWithTaskMetadataAsync(client, "multi-round-then-task");

        // Should have created a task after two MRTR rounds.
        Assert.NotNull(result.Task);
        Assert.Equal(2, elicitCount);

        var taskStatus = await WaitForTaskCompletionAsync(result.Task.TaskId);
        Assert.Equal(McpTaskStatus.Completed, taskStatus.Status);
    }

    [Fact]
    public async Task BackwardsCompat_ImmediateTaskCreation_WorksUnchanged()
    {
        StartServer();
        await using var client = await CreateMcpClientForServer(CreateClientOptions(new McpClientHandlers()));

        var result = await CallToolWithTaskMetadataAsync(client, "immediate-task-tool",
            new Dictionary<string, object?> { ["input"] = "test" });

        // Immediate task creation — result has task immediately.
        Assert.NotNull(result.Task);

        var taskStatus = await WaitForTaskCompletionAsync(result.Task.TaskId);
        Assert.Equal(McpTaskStatus.Completed, taskStatus.Status);
    }
}
