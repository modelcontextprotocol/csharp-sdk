---
title: Tasks
author: eiriktsarpalis
description: MCP Tasks for Long-Running Operations
uid: tasks
---

# MCP Tasks

> [!WARNING]
> Tasks are an **experimental feature** in the MCP specification (version 2025-11-25). The API may change in future releases. See the [Experimental APIs](../../experimental.md) documentation for details on working with experimental APIs.

The Model Context Protocol (MCP) supports [task-based execution] for long-running operations. Tasks enable a "call-now, fetch-later" pattern where clients can initiate operations that may take significant time to complete, then poll for status and retrieve results when ready.

[task-based execution]: https://modelcontextprotocol.io/specification/draft/basic/utilities/tasks

## Overview

Tasks are useful when operations may take a long time to complete, such as:

- Large dataset processing or analysis
- Complex report generation
- Code migration or refactoring operations
- Machine learning inference or training
- Batch data transformations

Without tasks, clients must keep connections open for the entire duration of long-running operations. Tasks allow clients to:

1. Initiate an operation and receive a task ID immediately
2. Disconnect and reconnect later
3. Poll for status updates
4. Retrieve results when complete
5. Cancel operations if needed

## Task Lifecycle

Tasks follow a defined lifecycle through these status values:

| Status | Description |
|--------|-------------|
| `working` | Task is actively being processed |
| `input_required` | Task is waiting for additional input (e.g., elicitation) |
| `completed` | Task finished successfully; results are available |
| `failed` | Task encountered an error |
| `cancelled` | Task was cancelled by the client |

Tasks begin in the `working` status and transition to one of the terminal states (`completed`, `failed`, or `cancelled`). Once in a terminal state, the status cannot change.

## Server Implementation

### Configuring Task Support

To enable task support on a server, configure a task store when setting up the MCP server:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Create a task store for managing task state
var taskStore = new InMemoryMcpTaskStore();

builder.Services.AddMcpServer(options =>
{
    // Enable tasks by providing a task store
    options.TaskStore = taskStore;
})
.WithHttpTransport()
.WithTools<MyTools>();
```

The <xref:ModelContextProtocol.InMemoryMcpTaskStore> is a reference implementation suitable for development and single-server deployments. For production multi-server scenarios, implement <xref:ModelContextProtocol.IMcpTaskStore> with a persistent backing store (database, Redis, etc.).

### Task Store Configuration

The `InMemoryMcpTaskStore` constructor accepts several optional parameters:

```csharp
var taskStore = new InMemoryMcpTaskStore(
    defaultTtl: TimeSpan.FromHours(1),        // Default task retention time
    maxTtl: TimeSpan.FromHours(24),           // Maximum allowed TTL
    pollInterval: TimeSpan.FromSeconds(1),    // Suggested client poll interval
    cleanupInterval: TimeSpan.FromMinutes(5), // Background cleanup frequency
    pageSize: 100,                            // Tasks per page for listing
    maxTasks: 1000,                           // Maximum total tasks allowed
    maxTasksPerSession: 100                   // Maximum tasks per session
);
```

### Tool Task Support

Tools automatically advertise task support when they return `Task`, `ValueTask`, `Task<T>`, or `ValueTask<T>`:

```csharp
[McpServerToolType]
public class MyTools
{
    // This tool automatically supports task-augmented calls
    // because it returns Task<string> (async method)
    [McpServerTool, Description("Processes a large dataset")]
    public static async Task<string> ProcessDataset(
        int recordCount,
        CancellationToken cancellationToken)
    {
        // Long-running operation
        await Task.Delay(5000, cancellationToken);
        return $"Processed {recordCount} records";
    }

    // Synchronous tools don't support task augmentation by default
    [McpServerTool, Description("Quick operation")]
    public static string QuickOperation(string input) => $"Result: {input}";
}
```

You can explicitly control task support using <xref:ModelContextProtocol.Server.McpServerToolCreateOptions>:

```csharp
// In Program.cs or configuration
builder.Services.AddMcpServer()
    .WithTools([
        McpServerTool.Create(
            (int count, CancellationToken ct) => ProcessAsync(count, ct),
            new McpServerToolCreateOptions
            {
                Name = "requiredTaskTool",
                Execution = new ToolExecution
                {
                    // Require clients to use task augmentation
                    TaskSupport = ToolTaskSupport.Required
                }
            })
    ]);
```

Task support levels:
- `Forbidden` (default for sync methods): Tool cannot be called with task augmentation
- `Optional` (default for async methods): Tool can be called with or without task augmentation
- `Required`: Tool must be called with task augmentation

### Explicit Task Creation with `IMcpTaskStore`

For more control over task lifecycle, tools can directly interact with <xref:ModelContextProtocol.IMcpTaskStore> and return an `McpTask`. This approach allows you to:

- Create a task and return immediately while work continues in the background
- Control exactly when and how task status and results are updated
- Integrate with external systems for task execution

Here's a simple example using `Task.Run` to schedule background work:

```csharp
[McpServerToolType]
public class MyTools(IMcpTaskStore taskStore)
{
    [McpServerTool]
    [Description("Starts a background job and returns a task for polling.")]
    public async Task<McpTask> StartBackgroundJob(
        [Description("Number of items to process")] int itemCount,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        // Create a task in the store - this records the task metadata
        var task = await taskStore.CreateTaskAsync(
            new McpTaskMetadata { TimeToLive = TimeSpan.FromMinutes(30) },
            context.JsonRpcRequest.Id!,
            context.JsonRpcRequest,
            context.Server.SessionId,
            cancellationToken);

        // Schedule work to run in the background (fire-and-forget)
        _ = Task.Run(async () =>
        {
            try
            {
                // Simulate long-running work
                await Task.Delay(TimeSpan.FromSeconds(10));
                var result = $"Processed {itemCount} items successfully";

                // Store the completed result
                await taskStore.StoreTaskResultAsync(
                    task.TaskId,
                    McpTaskStatus.Completed,
                    JsonSerializer.SerializeToElement(new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = result }]
                    }),
                    context.Server.SessionId);
            }
            catch (Exception ex)
            {
                // Mark task as failed on error
                await taskStore.StoreTaskResultAsync(
                    task.TaskId,
                    McpTaskStatus.Failed,
                    JsonSerializer.SerializeToElement(new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = ex.Message }],
                        IsError = true
                    }),
                    context.Server.SessionId);
            }
        }, CancellationToken.None);

        // Return immediately - client will poll for completion
        return task;
    }
}
```

When a tool returns `McpTask`, the SDK bypasses automatic task wrapping and returns the task directly to the client.

> [!IMPORTANT]
> **No Fault Tolerance Guarantees**: Both `InMemoryMcpTaskStore` and the automatic task support for `Task`-returning tool methods do **not** provide fault tolerance. Task state and execution are bounded by the memory of the server process. If the server crashes or restarts:
> - All in-memory task metadata is lost
> - Any in-flight task execution is terminated
> - Clients will receive errors when polling for previously created tasks
>
> For fault-tolerant task execution, see the [Fault-Tolerant Task Implementations](#fault-tolerant-task-implementations) section.

### Task Status Notifications

When `SendTaskStatusNotifications` is enabled, the server automatically sends status updates to connected clients:

```csharp
builder.Services.AddMcpServer(options =>
{
    options.TaskStore = taskStore;
    options.SendTaskStatusNotifications = true; // Enable notifications
});
```

Clients receive `notifications/tasks/status` messages when task status changes.

## Client Implementation

### Calling Tools as Tasks

To execute a tool as a task, include the `Task` property in the request:

```csharp
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var client = await McpClient.CreateAsync(transport);

// Call tool with task augmentation
var result = await client.CallToolAsync(
    new CallToolRequestParams
    {
        Name = "processDataset",
        Arguments = new Dictionary<string, JsonElement>
        {
            ["recordCount"] = JsonSerializer.SerializeToElement(1000)
        },
        Task = new McpTaskMetadata
        {
            TimeToLive = TimeSpan.FromHours(2) // Request 2-hour retention
        }
    },
    cancellationToken);

// Check if a task was created
if (result.Task != null)
{
    Console.WriteLine($"Task created: {result.Task.TaskId}");
    Console.WriteLine($"Status: {result.Task.Status}");
}
```

### Polling for Task Status

Use <xref:ModelContextProtocol.Client.McpClient.GetTaskAsync*> to check task status:

```csharp
var task = await client.GetTaskAsync(taskId, cancellationToken: cancellationToken);
Console.WriteLine($"Status: {task.Status}");
Console.WriteLine($"Last Updated: {task.LastUpdatedAt}");

if (task.StatusMessage != null)
{
    Console.WriteLine($"Message: {task.StatusMessage}");
}
```

### Waiting for Completion

The SDK provides helper methods for polling until a task completes:

```csharp
// Poll until task reaches terminal state
var completedTask = await client.PollTaskUntilCompleteAsync(
    taskId,
    cancellationToken: cancellationToken);

if (completedTask.Status == McpTaskStatus.Completed)
{
    // Get the result as raw JSON
    var resultJson = await client.GetTaskResultAsync(
        taskId,
        cancellationToken: cancellationToken);

    // Deserialize to the expected type
    var result = resultJson.Deserialize<CallToolResult>(McpJsonUtilities.DefaultOptions);

    foreach (var content in result?.Content ?? [])
    {
        if (content is TextContentBlock text)
        {
            Console.WriteLine(text.Text);
        }
    }
}
else if (completedTask.Status == McpTaskStatus.Failed)
{
    Console.WriteLine($"Task failed: {completedTask.StatusMessage}");
}
```

### Listing Tasks

List all tasks for the current session:

```csharp
var tasks = await client.ListTasksAsync(cancellationToken: cancellationToken);

foreach (var task in tasks)
{
    Console.WriteLine($"{task.TaskId}: {task.Status}");
}
```

### Cancelling Tasks

Cancel a running task:

```csharp
var cancelledTask = await client.CancelTaskAsync(
    taskId,
    cancellationToken: cancellationToken);

Console.WriteLine($"Task status: {cancelledTask.Status}"); // Cancelled
```

### Handling Status Notifications

Register a handler to receive real-time status updates:

```csharp
var options = new McpClientOptions
{
    Handlers = new McpClientHandlers
    {
        TaskStatusHandler = (task, cancellationToken) =>
        {
            Console.WriteLine($"Task {task.TaskId} status changed to {task.Status}");
            return ValueTask.CompletedTask;
        }
    }
};

var client = await McpClient.CreateAsync(transport, options);
```

> [!NOTE]
> Clients should not rely on receiving status notifications. Notifications are optional and may not be sent in all scenarios. Always use polling as the primary mechanism for tracking task status.

## Implementing a Custom Task Store

For production deployments, implement <xref:ModelContextProtocol.IMcpTaskStore> with a persistent backing store:

```csharp
public class DatabaseTaskStore : IMcpTaskStore
{
    private readonly IDbConnection _db;

    public DatabaseTaskStore(IDbConnection db) => _db = db;

    public async Task<McpTask> CreateTaskAsync(
        McpTaskMetadata taskMetadata,
        RequestId requestId,
        JsonRpcRequest request,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        var task = new McpTask
        {
            TaskId = Guid.NewGuid().ToString(),
            Status = McpTaskStatus.Working,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            TimeToLive = taskMetadata.TimeToLive ?? TimeSpan.FromHours(1)
        };

        // Store in database
        await _db.ExecuteAsync(
            "INSERT INTO Tasks (TaskId, SessionId, Status, ...) VALUES (@TaskId, @SessionId, @Status, ...)",
            new { task.TaskId, sessionId, task.Status, ... });

        return task;
    }

    public async Task<McpTask?> GetTaskAsync(
        string taskId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        // Retrieve from database with session isolation
        return await _db.QuerySingleOrDefaultAsync<McpTask>(
            "SELECT * FROM Tasks WHERE TaskId = @TaskId AND SessionId = @SessionId",
            new { taskId, sessionId });
    }

    // Implement other interface methods...
}
```

### Task Store Best Practices

1. **Session Isolation**: Always filter tasks by session ID to prevent cross-session access
2. **TTL Enforcement**: Implement background cleanup of expired tasks
3. **Thread Safety**: Ensure all operations are thread-safe for concurrent access
4. **Atomic Updates**: Use database transactions for status transitions
5. **Optimistic Concurrency**: Prevent lost updates with version checking or row locks

## Error Handling

Task operations may throw <xref:ModelContextProtocol.McpException> with these error codes:

| Error Code | Scenario |
|------------|----------|
| `InvalidParams` | Invalid or nonexistent task ID or invalid cursor |
| `InvalidParams` | Tool with `taskSupport: forbidden` called with task metadata, or tool with `taskSupport: required` called without task metadata |
| `InternalError` | Task execution failure or result unavailable |

Example error handling:

```csharp
try
{
    var task = await client.GetTaskAsync(taskId, cancellationToken: ct);
}
catch (McpProtocolException ex) when (ex.ErrorCode == McpErrorCode.InvalidParams)
{
    Console.WriteLine($"Task not found: {taskId}");
}
```

## Complete Example

<!-- TODO: Remove mlc-disable block after merging to main -->
<!-- mlc-disable -->
See the [LongRunningTasks sample](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/LongRunningTasks) for a complete working example demonstrating:
<!-- mlc-enable -->

- Server setup with a file-based `IMcpTaskStore` for durability
- Explicit task creation via `IMcpTaskStore` in tools returning `McpTask`
- Task polling and result retrieval across server restarts
- Cancellation support

## Fault-Tolerant Task Implementations

The default `InMemoryMcpTaskStore` and automatic task support for async tools are convenient for development, but they provide no durability or fault tolerance. When the server process terminates—whether due to a crash, deployment, or scaling event—all task state and in-flight computations are lost.

### Why Fault Tolerance Requires External Systems

True fault tolerance for long-running tasks requires two key capabilities that cannot be provided by an in-process solution:

1. **Durable Task State**: Task metadata (ID, status, results) must survive process termination. This requires an external persistent store such as a database, Redis, or distributed cache.

2. **Resumable Compute**: The actual work being performed must be executed by an external system that can continue running independently of the MCP server process—such as a job queue (Azure Service Bus, RabbitMQ), workflow engine (Temporal, Azure Durable Functions), or batch processing system (Azure Batch, Kubernetes Jobs).

### Explicit Task Creation with `IMcpTaskStore`

To implement fault-tolerant tasks, tools can directly interact with `IMcpTaskStore` and return an `McpTask` instead of relying on automatic task wrapping. This approach gives you full control over task lifecycle and enables integration with external compute fabrics:

```csharp
[McpServerToolType]
public class FaultTolerantTools(IMcpTaskStore taskStore, IJobQueue jobQueue)
{
    [McpServerTool]
    [Description("Submits a long-running job with fault-tolerant execution.")]
    public async Task<McpTask> SubmitJob(
        [Description("The job parameters")] string jobInput,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        // 1. Create a task in the durable store
        var task = await taskStore.CreateTaskAsync(
            new McpTaskMetadata { TimeToLive = TimeSpan.FromHours(24) },
            context.JsonRpcRequest.Id!,
            context.JsonRpcRequest,
            context.Server.SessionId,
            cancellationToken);

        // 2. Submit work to an external compute fabric
        // The job queue handles execution independently of this process
        await jobQueue.EnqueueAsync(new JobMessage
        {
            TaskId = task.TaskId,
            SessionId = context.Server.SessionId,
            Input = jobInput
        }, cancellationToken);

        // 3. Return the task immediately - client will poll for completion
        return task;
    }
}
```

The external job processor updates the task store when work completes:

```csharp
// In a separate worker process or Azure Function
public class JobProcessor(IMcpTaskStore taskStore)
{
    public async Task ProcessJobAsync(JobMessage job, CancellationToken cancellationToken)
    {
        try
        {
            // Perform the actual long-running work
            var result = await DoExpensiveWorkAsync(job.Input, cancellationToken);

            // Store the result in the durable task store
            await taskStore.StoreTaskResultAsync(
                job.TaskId,
                McpTaskStatus.Completed,
                JsonSerializer.SerializeToElement(new CallToolResult
                {
                    Content = [new TextContentBlock { Text = result }]
                }),
                job.SessionId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Mark task as failed
            await taskStore.StoreTaskResultAsync(
                job.TaskId,
                McpTaskStatus.Failed,
                JsonSerializer.SerializeToElement(new CallToolResult
                {
                    Content = [new TextContentBlock { Text = ex.Message }],
                    IsError = true
                }),
                job.SessionId,
                cancellationToken);
        }
    }
}
```

### Simplified Example: File-Based Task Store

<!-- TODO: Remove mlc-disable block after merging to main -->
<!-- mlc-disable -->
The [LongRunningTasks sample](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/LongRunningTasks) demonstrates a simplified fault-tolerant approach using the file system. The `FileBasedMcpTaskStore` persists task state to disk, allowing tasks to survive server restarts:
<!-- mlc-enable -->

```csharp
// Use a file-based task store for durability
var taskStorePath = Path.Combine(Path.GetTempPath(), "mcp-tasks");
var taskStore = new FileBasedMcpTaskStore(taskStorePath);

builder.Services.AddMcpServer(options =>
{
    options.TaskStore = taskStore;
})
.WithHttpTransport()
.WithTools<TaskTools>();
```

The sample's tool returns an `McpTask` directly by calling `CreateTaskAsync`:

```csharp
[McpServerToolType]
public class TaskTools(IMcpTaskStore taskStore)
{
    [McpServerTool]
    [Description("Submits a job and returns a task that can be polled for completion.")]
    public async Task<McpTask> SubmitJob(
        [Description("A label for the job")] string jobName,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        return await taskStore.CreateTaskAsync(
            new McpTaskMetadata { TimeToLive = TimeSpan.FromMinutes(10) },
            context.JsonRpcRequest.Id!,
            context.JsonRpcRequest,
            context.Server.SessionId,
            cancellationToken);
    }
}
```

While this file-based approach demonstrates the pattern, production systems should use proper distributed storage and compute infrastructure for true fault tolerance and scalability.

## See Also

- <xref:ModelContextProtocol.IMcpTaskStore>
- <xref:ModelContextProtocol.InMemoryMcpTaskStore>
- <xref:ModelContextProtocol.Protocol.McpTask>
- <xref:ModelContextProtocol.Protocol.McpTaskStatus>
- [MCP Tasks Specification](https://modelcontextprotocol.io/specification/draft/basic/utilities/tasks)
