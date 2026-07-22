# Task-Aware Show Producer

A server-only sample for the experimental MCP Tasks extension. One attributed tool works in both modes:

- Visual Studio and other clients without a Tasks opt-in run it normally and receive the usual elicitation request.
- A `2026-07-28` client that opts into `io.modelcontextprotocol/tasks` immediately receives a task ID, polls status, resolves elicitation through `tasks/update`, and can cancel with `tasks/cancel`.

No custom client is included.

## What it shows

- `.WithTasks(new InMemoryMcpTaskStore())` automatically advertises and wires the Tasks extension.
- Per-request opt-in preserves normal tool behavior for clients that do not understand Tasks.
- `ElicitAsync` inside the tool becomes a task `input_required` state when task execution is active.
- The tool's `CancellationToken` is signaled by `tasks/cancel`.
- A standard tool result, including tool-level errors, becomes the task's terminal `completed` payload.

This deliberately contrasts with `ReleasePlannerServer`: Tasks keep the background handler alive, while MRTR moves all continuation state through `requestState`.

## Visual Studio demo

Build once before Visual Studio starts the server:

```powershell
dotnet build samples\ShowProducerServer\ShowProducerServer.csproj
```

This appendix sample is not registered in the repository `.mcp.json`, keeping the live presentation tool picker focused. Add it to a client configuration when demonstrating the inline fallback or testing with a Tasks-aware host.

When using a task-aware host, the same invocation is returned as a pollable task. The in-memory store is appropriate for this local demo; production servers should use a durable `IMcpTaskStore`.
