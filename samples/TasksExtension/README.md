# Tasks Extension Sample

Demonstrates the MCP tasks extension ([SEP-2663](https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md)) end-to-end in a single process.

The server is configured with an in-memory `IMcpTaskStore`, which is sufficient to make any
`[McpServerTool]` method automatically run as a background task when the client opts into the
tasks extension on a per-request basis.

The client invokes the same `run-report` tool two ways:

1. **`CallToolAsync` (auto-poll)** — the SDK injects the opt-in marker into `_meta`, polls
   `tasks/get` at the cadence the server suggests, dispatches any input requests through
   registered handlers, and returns the final `CallToolResult` (or throws on a terminal
   `Failed`/`Cancelled`).
2. **`CallToolRawAsync` (manual poll)** — the caller receives the raw
   `ResultOrCreatedTask<CallToolResult>` and drives the lifecycle directly with
   `GetTaskAsync`, `UpdateTaskAsync`, and `CancelTaskAsync`. Useful when you need to surface
   progress to a UI or stream task status updates between polls.

Both ends of the conversation are connected in-process over an in-memory `Pipe`, so no separate
server process or HTTP transport is required.

## Run

```bash
dotnet run --project samples/TasksExtension/TasksExtension.csproj
```

Expected output:

```
=== CallToolAsync (auto-poll) ===
  result: report ready

=== CallToolRawAsync (manual poll) ===
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
