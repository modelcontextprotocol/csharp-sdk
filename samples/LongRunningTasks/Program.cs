// This sample demonstrates using a custom IMcpTaskStore implementation for
// durable task storage. The FileBasedMcpTaskStore persists tasks to disk,
// allowing them to survive server restarts.
//
// To test:
// 1. Start the server and call the SubmitJob tool
// 2. Poll the returned task using tasks/get
// 3. Optionally restart the server - the task will still be queryable

using LongRunningTasks;
using LongRunningTasks.Tools;

var builder = WebApplication.CreateBuilder(args);

// Use a file-based task store for persistence across server restarts.
// Tasks survive server restarts and can be resumed or queried after a crash.
var taskStorePath = Path.Combine(Path.GetTempPath(), "mcp-tasks");
var taskStore = new FileBasedMcpTaskStore(taskStorePath);

builder.Services.AddMcpServer(options =>
{
    options.TaskStore = taskStore;
    options.ServerInfo = new()
    {
        Name = "LongRunningTasksServer",
        Version = "1.0.0"
    };
})
.WithHttpTransport()
.WithTools<TaskTools>();

var app = builder.Build();
app.MapMcp();
app.Run();
