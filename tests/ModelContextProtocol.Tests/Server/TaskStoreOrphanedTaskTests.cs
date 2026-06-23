using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Runtime.InteropServices;
using System.Text.Json;

#pragma warning disable MCPEXP001

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Verifies that when both <see cref="McpServerOptions.TaskStore"/> and
/// <see cref="McpServerHandlers.CallToolWithTaskHandler"/> are configured and the handler returns
/// <see cref="CreateTaskResult"/> (IsTask = true), the store's pre-created task is failed with a
/// clear error rather than being orphaned in <see cref="McpTaskStatus.Working"/> forever.
/// </summary>
public class TaskStoreOrphanedTaskTests : ClientServerTestBase
{
    public TaskStoreOrphanedTaskTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
#if !NET
        Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "https://github.com/modelcontextprotocol/csharp-sdk/issues/587");
#endif
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.Services.Configure<McpServerOptions>(options =>
        {
            options.Capabilities ??= new ServerCapabilities();
            options.TaskStore = new InMemoryMcpTaskStore();

            // Returning IsTask = true here while TaskStore is also configured is the
            // misconfiguration the server must guard against.
            options.Handlers.CallToolWithTaskHandler = (context, cancellationToken) =>
                new ValueTask<ResultOrCreatedTask<CallToolResult>>(new CreateTaskResult
                {
                    TaskId = "user-task",
                    Status = McpTaskStatus.Working,
                    CreatedAt = DateTimeOffset.UtcNow,
                    LastUpdatedAt = DateTimeOffset.UtcNow,
                });
        });
    }

    [Fact]
    public async Task TaskStoreAndHandler_BothCreatingTasks_FailsStoreTaskWithClearError()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        // The store's task is created synchronously and its taskId returned to the client.
        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "anything" }, ct);

        Assert.True(augmented.IsTask);
        var taskId = augmented.TaskCreated!.TaskId;

        // Poll until the background runner observes the handler's IsTask=true and fails the
        // store's task. Without the fix this would loop forever in Working.
        GetTaskResult? taskResult = null;
        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(50, ct);
            taskResult = await client.GetTaskAsync(taskId, ct);
            if (taskResult is FailedTaskResult)
            {
                break;
            }
        }

        var failed = Assert.IsType<FailedTaskResult>(taskResult);
        Assert.Equal(JsonValueKind.Object, failed.Error.ValueKind);
        Assert.Equal((int)McpErrorCode.InternalError, failed.Error.GetProperty("code").GetInt32());

        var message = failed.Error.GetProperty("message").GetString();
        Assert.NotNull(message);
        Assert.Contains(nameof(McpServerOptions.TaskStore), message);
        Assert.Contains(nameof(McpServerHandlers.CallToolWithTaskHandler), message);
    }
}
