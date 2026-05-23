---
title: Tasks
description: MCP Tasks for Long-Running Operations
uid: tasks
---

# MCP Tasks

> **Status**: Experimental (`MCPEXP001`). Based on [SEP-2663](https://github.com/nicholasgasior/specification/blob/main/docs/specification/2025-03-26/extensions/tasks.md).

Tasks allow MCP servers to run tool invocations asynchronously, reporting progress and requesting additional input from the client while execution continues in the background.

## Overview

When a client calls a tool and includes the `io.modelcontextprotocol/tasks` extension key in `_meta`, the server may return a `CreateTaskResult` instead of an immediate `CallToolResult`. The client then polls via `tasks/get` until the task reaches a terminal state.

### Task Lifecycle

```
Working → Completed
Working → Failed
Working → Cancelled
Working → InputRequired → Working (loop)
```

## Server Configuration

### Using the Task Store

The `InMemoryMcpTaskStore` provides a ready-to-use in-memory implementation:

```csharp
var builder = McpServerBuilder.Create(options =>
{
    options.TaskStore = new InMemoryMcpTaskStore();
});
builder.WithTools<MyTools>();
```

When a `TaskStore` is configured:
- `tasks/get`, `tasks/update`, and `tasks/cancel` handlers are auto-wired from the store.
- Built-in tools are automatically wrapped: if the client signals task support, the tool is offloaded to a background task via the store.
- Server-initiated requests (elicitation, sampling) are redirected through the store's input request mechanism while inside a task scope.

### Custom Task Handlers

For full control without a store, set handlers directly:

```csharp
options.Handlers.GetTaskHandler = async (request, ct) => { ... };
options.Handlers.UpdateTaskHandler = async (request, ct) => { ... };
options.Handlers.CancelTaskHandler = async (request, ct) => { ... };
```

### Task-Aware Tool Handlers

The `CallToolWithTaskHandler` returns `ResultOrCreatedTask<CallToolResult>`, allowing the handler to return either an immediate result or a task:

```csharp
options.Handlers.CallToolWithTaskHandler = async (request, ct) =>
{
    // Return immediate result
    return new CallToolResult { ... };

    // Or return a task
    return new CreateTaskResult { TaskId = "...", Status = McpTaskStatus.Working, ... };
};
```

> **Note**: `CallToolHandler` and `CallToolWithTaskHandler` are mutually exclusive. If both are set, an exception is thrown.

### Task Scope for Server-Initiated Requests

When executing tool logic as a background task, use `CreateMcpTaskScope` to redirect elicitation/sampling/roots requests through the task store:

```csharp
using (server.CreateMcpTaskScope(taskId, taskStore))
{
    // Any ElicitAsync/SampleAsync calls here will be stored as
    // input requests and await client responses via tasks/update.
    var result = await server.ElicitAsync(...);
}
```

## Client Usage

### Automatic Polling

`CallToolAsync` handles the full lifecycle automatically:

```csharp
var result = await client.CallToolAsync(new CallToolRequestParams
{
    Name = "long-running-tool",
    Arguments = { ... },
}, cancellationToken);
// Blocks until completed, resolving input requests along the way.
```

### Manual Control

Use `CallToolRawAsync` for manual lifecycle management:

```csharp
var raw = await client.CallToolRawAsync(requestParams, cancellationToken);
if (raw.IsTask)
{
    // Poll manually via client.GetTaskAsync(raw.TaskCreated!.TaskId, ...)
}
```

## Input Requests (Multi-Round-Trip)

Per [SEP-2322 (MRTR)](https://modelcontextprotocol.io/seps/2322-MRTR), tasks can request additional input from the client. The server adds input requests to the store, and the client provides responses via `tasks/update`.

Supported input request types:
- **Elicitation** (`elicitation/create`)
- **Sampling** (`sampling/createMessage`)

The client deduplicates input requests across polling cycles to avoid re-resolving the same request.

## Architecture Notes

### Filter Model (3 Cases)

1. **Non-task filter + non-task handler**: Filters applied normally, final result converted to task shape.
2. **Task filter + task handler**: Filters applied directly to the task-augmented handler.
3. **Mixed**: Throws `InvalidOperationException`.

### Immutable Store Design

`InMemoryMcpTaskStore` uses immutable records with compare-and-swap (CAS) updates for lock-free thread safety. `ImmutableDictionary<string, JsonElement>` is used for input requests/responses.

## Known Limitations / TODOs

- **Task status notifications (SEP-2575)**: Server-to-client push notifications for task state changes are not yet implemented. The client currently relies on polling only.
- **Lazy task creation**: Currently, `CreateTaskAsync` is called eagerly before the inner handler runs. Ideally, task creation should be deferred until the handler actually needs it (avoids unnecessary store writes for tools that return immediately).
- **Mid-execution promotion to task**: There is currently no way for a tool to start executing synchronously and then transition the remaining work to a background task. A user can achieve this manually with a custom `CallToolWithTaskHandler`, but there is no built-in support for `[McpServerTool]`-attributed methods to say "the remaining work should continue as a task." This could be addressed with an API like `McpServer.PromoteToTaskAsync()` callable from within tool execution.
- **Extensions serialization round-trip**: `ServerCapabilities.Extensions` (backed by `IDictionary<string, object>`) does not survive JSON round-trip via source-generated serialization. The `object` values cannot be deserialized by the source generator.

