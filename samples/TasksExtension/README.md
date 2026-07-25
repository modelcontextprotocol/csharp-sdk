# Tasks Extension Sample

Demonstrates the MCP tasks extension ([SEP-2663](https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md)) end-to-end in a single process.

The server is configured with an in-memory `IMcpTaskStore`, which is sufficient to make any
`[McpServerTool]` method automatically run as a background task when the client opts into the
tasks extension on a per-request basis.

The client invokes the `run-report` tool with **`CallToolAsTaskAsync` (manual poll)**. It
receives a `ResultOrCreatedTask<CallToolResult>` and, when the server runs the call as a
background task, drives the lifecycle directly with `GetTaskAsync` — polling at the server's
suggested cadence until the task reaches a terminal state (`Completed`, `Failed`, or
`Cancelled`). If the server returns an inline result instead of creating a task, the sample
surfaces that result and stops.

Both ends of the conversation are connected in-process over an in-memory `Pipe`, so no separate
server process or HTTP transport is required.

## Run

```bash
dotnet run --project samples/TasksExtension/TasksExtension.csproj
```

Expected output:

```
=== CallToolAsTaskAsync (manual poll) ===
  task created: id=… status=Working pollIntervalMs=250
  poll 1: still working …
  …
  task completed after N poll(s): report ready
```

## Notes

- The `MCPEXP001` warning is suppressed because the tasks extension is still experimental. The
  project's `<NoWarn>` already includes it; if you copy this pattern into your own project,
  either suppress the diagnostic or wrap the experimental APIs in
  `#pragma warning disable MCPEXP001`.
- For production deployments — especially stateless HTTP servers — implement
  `IMcpTaskStore` against durable storage and register it as a singleton (see
  [docs/concepts/tasks/tasks.md](../../docs/concepts/tasks/tasks.md) for the contract).
