using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Runtime.InteropServices;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Verifies the runtime validation that fires when a handler opts into task-augmented execution
/// (returns a <see cref="CreateTaskResult"/>) without the server having any <c>tasks/get</c>
/// handler registered.
/// </summary>
public class TaskHandlerConfigurationValidationTests : ClientServerTestBase
{
    public TaskHandlerConfigurationValidationTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
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

            // Intentionally configure a task-augmented handler without TaskStore or any of the
            // task lifecycle handlers (GetTaskHandler/UpdateTaskHandler/CancelTaskHandler).
            options.Handlers.CallToolWithTaskHandler = (context, cancellationToken) =>
                new ValueTask<ResultOrCreatedTask<CallToolResult>>(new CreateTaskResult
                {
                    TaskId = "orphan-task",
                    Status = McpTaskStatus.Working,
                    CreatedAt = DateTimeOffset.UtcNow,
                    LastUpdatedAt = DateTimeOffset.UtcNow,
                });
        });
    }

    [Fact]
    public async Task CallTool_ReturningCreateTaskResult_WithoutTasksGetHandler_ThrowsAtRequestTime()
    {
        await using var client = await CreateMcpClientForServer();

        // Client surfaces a generic protocol error (the server intentionally redacts the message
        // on the wire), so use the base McpException type and confirm via server-side logs that
        // the originating exception was the misconfiguration guard.
        await Assert.ThrowsAnyAsync<McpException>(async () =>
            await client.CallToolAsync(
                new CallToolRequestParams { Name = "anything" },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(MockLoggerProvider.LogMessages, log =>
            log.Exception is InvalidOperationException ioe &&
            ioe.Message.Contains("tasks/get", StringComparison.Ordinal) &&
            ioe.Message.Contains("CreateTaskResult", StringComparison.Ordinal));
    }
}
