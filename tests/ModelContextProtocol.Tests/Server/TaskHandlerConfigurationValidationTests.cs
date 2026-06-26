using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Runtime.InteropServices;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Verifies behavior when a handler returns a <see cref="CreateTaskResult"/> alternate without
/// the server having any <c>tasks/get</c> handler registered. After the ResultOrAlternate
/// generalization, the Core server no longer guards against this -- the extension is responsible
/// for ensuring lifecycle handlers are registered.
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

            // Configure a task-augmented handler without TaskStore or any of the
            // task lifecycle handlers (GetTaskHandler/UpdateTaskHandler/CancelTaskHandler).
            options.Handlers.CallToolWithAlternateHandler = (context, cancellationToken) =>
                new ValueTask<ResultOrAlternate<CallToolResult>>(new CreateTaskResult
                {
                    TaskId = "orphan-task",
                    Status = McpTaskStatus.Working,
                    CreatedAt = DateTimeOffset.UtcNow,
                    LastUpdatedAt = DateTimeOffset.UtcNow,
                });
        });
    }

    [Fact]
    public async Task ServerAcceptsAlternateHandler_WithoutTasksGetHandler_NoStartupError()
    {
        // The Core guard that previously threw InvalidOperationException at request time when
        // a CallToolWithAlternateHandler returned a CreateTaskResult without tasks/get being
        // registered has been removed. The extension is now responsible for that guarantee.
        // This test verifies the server starts and connects successfully with such configuration.
        await using var client = await CreateMcpClientForServer();

        // If we get here, the server accepted the handler config without error.
        Assert.NotNull(client);
    }
}
