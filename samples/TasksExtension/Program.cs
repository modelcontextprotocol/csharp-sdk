// Demonstrates the MCP tasks extension (SEP-2663):
//
//   - A server is configured with .WithTasks(store) so that any [McpServerTool] invocation
//     becomes a background task when the client opts in via the per-request _meta marker.
//   - The client invokes the tool and manually drives the lifecycle via GetTaskAsync.
//
// Both server and client are wired together in-process over an in-memory pipe so the sample
// is self-contained — no separate server process or HTTP transport required.

#pragma warning disable MCPEXP001, MCPEXP002, MCPEXP004

using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.IO.Pipelines;
using System.Text.Json;

Pipe clientToServerPipe = new(), serverToClientPipe = new();

var store = new InMemoryMcpTaskStore { DefaultPollIntervalMs = 250 };

var services = new ServiceCollection();
services.AddMcpServer()
    .WithTools([McpServerTool.Create(SlowTools.RunReport, new() { Name = "run-report" })])
    .WithTasks(store);
services.AddSingleton<ITransport>(new StreamServerTransport(clientToServerPipe.Reader.AsStream(), serverToClientPipe.Writer.AsStream()));

await using var serviceProvider = services.BuildServiceProvider();
var server = serviceProvider.GetRequiredService<McpServer>();
_ = server.RunAsync();

await using McpClient client = await McpClient.CreateAsync(
    new StreamClientTransport(
        serverInput: clientToServerPipe.Writer.AsStream(),
        serverOutput: serverToClientPipe.Reader.AsStream()));

Console.WriteLine("=== CallToolAsTaskAsync (manual poll) ===");
var raw = await client.CallToolAsTaskAsync(new CallToolRequestParams { Name = "run-report" });
if (!raw.IsTask)
{
    // Either the server doesn't advertise the tasks extension or it chose to run the call
    // synchronously despite the client opt-in. Surface the inline result and stop.
    Console.WriteLine($"  result (inline): {((TextContentBlock)raw.Result!.Content[0]).Text}");
    return;
}

CreateTaskResult created = raw.TaskCreated!;
Console.WriteLine($"  task created: id={created.TaskId} status={created.Status} pollIntervalMs={created.PollIntervalMs}");

long pollIntervalMs = created.PollIntervalMs ?? 1000;
int pollCount = 0;
while (true)
{
    await Task.Delay(TimeSpan.FromMilliseconds(pollIntervalMs));
    pollCount++;

    var state = await client.GetTaskAsync(created.TaskId);
    if (state.PollIntervalMs is { } updated)
    {
        pollIntervalMs = updated;
    }

    switch (state)
    {
        case CompletedTaskResult completed:
            // The Result property carries the wrapped CallToolResult as a raw JsonElement.
            var callToolResult = completed.Result.Deserialize<CallToolResult>()!;
            Console.WriteLine($"  task completed after {pollCount} poll(s): {((TextContentBlock)callToolResult.Content[0]).Text}");
            return;

        case FailedTaskResult failed:
            Console.WriteLine($"  task failed: {failed.Error}");
            return;

        case CancelledTaskResult:
            Console.WriteLine("  task was cancelled");
            return;

        case WorkingTaskResult:
            Console.WriteLine($"  poll {pollCount}: still working …");
            continue;

        case InputRequiredTaskResult inputRequired:
            Console.WriteLine($"  poll {pollCount}: input requested ({inputRequired.InputRequests?.Count ?? 0} key(s))");
            continue;
    }
}

internal static class SlowTools
{
    [Description("Runs a short simulated report and returns when it's done.")]
    public static async Task<string> RunReport(CancellationToken cancellationToken)
    {
        // Real-world workloads would do meaningful work here; we just sleep so the polling
        // path is observable in the console output.
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        return "report ready";
    }
}
