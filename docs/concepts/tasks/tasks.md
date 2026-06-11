---
title: Tasks
description: Run long-running tool invocations asynchronously with status polling and input requests.
uid: tasks
---

## Tasks

Tasks let an MCP server run a request asynchronously and report its result to the client later. The
primary use case today is long-running tool invocations: the tool is offloaded to a background task,
and the client polls for status, optionally exchanging additional input along the way.

> **Status**: Experimental — diagnostic ID `MCPEXP001`. The implementation tracks
> [SEP-2663 (Tasks Extension)](https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md).
> See [Experimental APIs](xref:experimental) for how to opt in.

### Overview

A client opts into tasks on a per-request basis by including the `io.modelcontextprotocol/tasks`
extension key in the request's `_meta`. When that opt-in is present, the server **may** respond
with a <xref:ModelContextProtocol.Protocol.CreateTaskResult> instead of the standard result
(e.g., <xref:ModelContextProtocol.Protocol.CallToolResult>). The client then polls `tasks/get`
until the task reaches a terminal state.

Per the SEP, the server **must not** return `CreateTaskResult` for a request that did not include
the extension opt-in. The SDK enforces this on the server side.

#### Task lifecycle

```text
                  ┌─────────────────────────┐
                  ▼                         │
   (start) → Working ──→ InputRequired      │
              │              │              │
              │              └──────────────┘  (client responds via tasks/update)
              │
              ├──→ Completed   (terminal — includes tool results with isError: true)
              ├──→ Cancelled   (terminal)
              └──→ Failed      (terminal — JSON-RPC errors only)
```

<xref:ModelContextProtocol.Protocol.McpTaskStatus> wire values are serialized in snake_case:
`working`, `input_required`, `completed`, `cancelled`, `failed`.

The discriminator field <xref:ModelContextProtocol.Protocol.Result.ResultType?displayProperty=nameWithType>
on the response payload is `"task"` for <xref:ModelContextProtocol.Protocol.CreateTaskResult>
and `"complete"` for ordinary results.

### Server configuration

#### Using the task store

The easiest way to enable tasks is to set an <xref:ModelContextProtocol.Server.IMcpTaskStore>
on <xref:ModelContextProtocol.Server.McpServerOptions.TaskStore?displayProperty=nameWithType>.
The SDK ships <xref:ModelContextProtocol.Server.InMemoryMcpTaskStore> for development and tests:

```csharp
#pragma warning disable MCPEXP001

builder.Services.AddMcpServer(options =>
{
    options.TaskStore = new InMemoryMcpTaskStore();
})
.WithTools<MyTools>();
```

When a `TaskStore` is configured the SDK automatically:

- Wires `tasks/get`, `tasks/update`, and `tasks/cancel` handlers from the store. Explicit
  handlers in <xref:ModelContextProtocol.Server.McpServerOptions.Handlers> still take precedence
  for any slot they fill.
- Advertises the `io.modelcontextprotocol/tasks` extension in
  <xref:ModelContextProtocol.Protocol.ServerCapabilities.Extensions?displayProperty=nameWithType>.
- Wraps each `[McpServerTool]` invocation so that, when the client opts in to the extension,
  the tool is offloaded to a background task tracked by the store.
- Establishes a task scope so that <xref:ModelContextProtocol.Server.McpServer.ElicitAsync*>,
  <xref:ModelContextProtocol.Server.McpServer.SampleAsync*>, and
  <xref:ModelContextProtocol.Server.McpServer.RequestRootsAsync*> called from inside the tool
  surface as entries in the task's `inputRequests` instead of as direct JSON-RPC requests.
- Plumbs a `CancellationToken` through to the tool that fires when the client invokes
  `tasks/cancel`, so cancellation propagates cooperatively.

For production scenarios that need durability, session isolation, multi-process routing, or
TTL-based cleanup, implement <xref:ModelContextProtocol.Server.IMcpTaskStore> yourself
(see [Implementing a custom task store](#implementing-a-custom-task-store) below).

#### Custom task handlers

For full control without a store, set the handlers directly. Each handler is an
<xref:ModelContextProtocol.Server.McpRequestHandler`2> that receives an
<xref:ModelContextProtocol.Server.RequestContext`1> with typed parameters:

```csharp
options.Handlers.GetTaskHandler = (context, ct) =>
{
    var taskId = context.Params!.TaskId;
    // … look up state and return one of the GetTaskResult subtypes.
    return new ValueTask<GetTaskResult>(new WorkingTaskResult { TaskId = taskId, /* … */ });
};

options.Handlers.UpdateTaskHandler = (context, ct) => /* return ValueTask<UpdateTaskResult> */;
options.Handlers.CancelTaskHandler = (context, ct) => /* return ValueTask<CancelTaskResult> */;
```

> **Important**: configure all three lifecycle handlers (or use a `TaskStore`) before opting
> into task responses. If a tool handler returns a `CreateTaskResult` but no `tasks/get`
> handler is wired, the server throws `InvalidOperationException` at request time so misconfigured
> deployments fail loudly instead of shipping unpollable tasks.

#### Returning a task from a tool handler

<xref:ModelContextProtocol.Server.McpServerHandlers.CallToolWithTaskHandler?displayProperty=nameWithType>
returns <xref:ModelContextProtocol.Protocol.ResultOrCreatedTask`1>, so each invocation can choose
between an immediate result and a background task:

```csharp
options.Handlers.CallToolWithTaskHandler = async (context, ct) =>
{
    if (ShouldRunInline(context.Params!))
    {
        return new CallToolResult { Content = [/* … */] };
    }

    var taskId = await StartBackgroundWorkAsync(context.Params!, ct);
    return new CreateTaskResult
    {
        TaskId = taskId,
        Status = McpTaskStatus.Working,
        CreatedAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow,
        PollIntervalMs = 1000,
    };
};
```

> <xref:ModelContextProtocol.Server.McpServerHandlers.CallToolHandler?displayProperty=nameWithType>
> and <xref:ModelContextProtocol.Server.McpServerHandlers.CallToolWithTaskHandler?displayProperty=nameWithType>
> are mutually exclusive. Setting one while the other is already non-null throws
> `InvalidOperationException` at the property setter.

#### Task scope for server-initiated requests

When you start background work from a custom <xref:ModelContextProtocol.Server.McpServerHandlers.CallToolWithTaskHandler?displayProperty=nameWithType>
(rather than the SDK's auto-wrapping), use <xref:ModelContextProtocol.Server.McpServer.CreateMcpTaskScope*>
to route elicitation, sampling, and `roots/list` calls through the task store as input requests
instead of direct JSON-RPC messages:

```csharp
using (server.CreateMcpTaskScope(taskId, taskStore))
{
    // ElicitAsync/SampleAsync/RequestRootsAsync calls in here are surfaced as
    // entries in the task's inputRequests, then await client responses via tasks/update.
    var elicit = await server.ElicitAsync(elicitParams, ct);
}
```

`CreateMcpTaskScope` returns an `IDisposable` that restores the prior ambient context on
`Dispose`. The scope is established automatically for `[McpServerTool]` methods that run via
`McpServerOptions.TaskStore`, so this API is only needed for custom handlers.

### Client usage

#### Automatic polling

<xref:ModelContextProtocol.Client.McpClient.CallToolAsync(ModelContextProtocol.Protocol.CallToolRequestParams,System.Threading.CancellationToken)>
handles the full task lifecycle automatically:

- Injects the `io.modelcontextprotocol/tasks` extension capability into the request's `_meta`.
- Polls `tasks/get` at the cadence the server suggests via `pollIntervalMs`.
- Dispatches input requests through the client's registered handlers
  (<xref:ModelContextProtocol.Client.McpClientHandlers.SamplingHandler> and
  <xref:ModelContextProtocol.Client.McpClientHandlers.ElicitationHandler>).
- Deduplicates already-resolved input request keys across polls so each request is handled at
  most once.
- Returns the final <xref:ModelContextProtocol.Protocol.CallToolResult> when the task completes,
  or throws <xref:ModelContextProtocol.McpException> on `Failed`/`Cancelled`.

```csharp
var result = await client.CallToolAsync(
    new CallToolRequestParams { Name = "long-running-tool", Arguments = arguments },
    cancellationToken);
```

#### Manual control

Use <xref:ModelContextProtocol.Client.McpClient.CallToolRawAsync*> to receive the raw
<xref:ModelContextProtocol.Protocol.ResultOrCreatedTask`1> without auto-polling, then drive the
lifecycle yourself using <xref:ModelContextProtocol.Client.McpClient.GetTaskAsync*>,
<xref:ModelContextProtocol.Client.McpClient.UpdateTaskAsync*>, and
<xref:ModelContextProtocol.Client.McpClient.CancelTaskAsync*>:

```csharp
var raw = await client.CallToolRawAsync(requestParams, cancellationToken);
if (raw.IsTask)
{
    var taskId = raw.TaskCreated!.TaskId;
    while (true)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(raw.TaskCreated.PollIntervalMs ?? 1000), cancellationToken);
        var state = await client.GetTaskAsync(taskId, cancellationToken);
        // Handle InputRequiredTaskResult by calling UpdateTaskAsync,
        // CompletedTaskResult by deserializing its Result property, etc.
    }
}
```

#### Stuck-task detector

`CallToolAsync` includes a safety net for misbehaving servers: if the task stays in
<xref:ModelContextProtocol.Protocol.McpTaskStatus.InputRequired> across many consecutive polls
without exposing any new input request keys (i.e. every previously requested input has already
been resolved by the client and yet the server keeps returning `InputRequired`), the client
gives up, issues a best-effort `tasks/cancel`, and throws
<xref:ModelContextProtocol.McpException>. This guards against a server that never transitions
out of `InputRequired` and prevents an unbounded poll loop.

The threshold defaults to `60` consecutive stuck polls and is configurable via
<xref:ModelContextProtocol.Client.McpClientOptions.MaxConsecutiveStuckPolls>. The effective
wall-clock timeout is roughly `MaxConsecutiveStuckPolls * pollIntervalMs`, so tune the option
with the server-side poll cadence in mind. Setting it too low risks false positives for servers
that are slow to surface follow-up input requests; setting it too high can mask misbehaving
servers.

### Input requests (multi-round-trip)

When a task needs additional input from the client, the server transitions it to
<xref:ModelContextProtocol.Protocol.McpTaskStatus.InputRequired> and returns the outstanding
requests in <xref:ModelContextProtocol.Protocol.InputRequiredTaskResult.InputRequests>. Each
entry is an arbitrary key paired with a `{ method, params }` envelope representing an
equivalent standalone server-to-client request. The client provides answers via
<xref:ModelContextProtocol.Client.McpClient.UpdateTaskAsync*>, keyed by the same identifiers.

Supported input request methods:

| Method | Dispatched to the client handler |
| --- | --- |
| `elicitation/create` | <xref:ModelContextProtocol.Client.McpClientHandlers.ElicitationHandler> |
| `sampling/createMessage` | <xref:ModelContextProtocol.Client.McpClientHandlers.SamplingHandler> |

Per SEP-2663:

- Each input request key **must** be unique over the lifetime of the task.
- Clients **should** deduplicate keys across polls so a request is only presented to the user
  or model once. `CallToolAsync` does this automatically.
- Servers **should** ignore `inputResponses` entries whose key does not currently correspond to
  an outstanding request, including responses for terminal-state tasks.
  <xref:ModelContextProtocol.Server.InMemoryMcpTaskStore> follows this rule.

### Implementing a custom task store

Implement <xref:ModelContextProtocol.Server.IMcpTaskStore> for production scenarios. Key
requirements drawn from the SEP and the SDK contract:

1. **Thread safety** — every method may be called concurrently.
2. **Idempotent terminal transitions** —
   <xref:ModelContextProtocol.Server.IMcpTaskStore.SetCompletedAsync*>,
   <xref:ModelContextProtocol.Server.IMcpTaskStore.SetFailedAsync*>, and
   <xref:ModelContextProtocol.Server.IMcpTaskStore.SetCancelledAsync*> must be no-ops on a task
   that is already in a terminal state so a late cancellation cannot overwrite a result.
3. **`InputResponseReceived` event** — after persisting an input response inside
   <xref:ModelContextProtocol.Server.IMcpTaskStore.ResolveInputRequestsAsync*>, raise
   <xref:ModelContextProtocol.Server.IMcpTaskStore.InputResponseReceived?displayProperty=nameWithType>
   for each resolved entry. This is the only mechanism that wakes a pending
   `server.ElicitAsync`/`server.SampleAsync` call waiting inside a task scope. In distributed
   deployments where a different server instance receives the `tasks/update`, the event must
   be propagated to the originating server (for example via Redis pub/sub, SignalR, or a custom
   transport).
4. **Strong-consistency on `CreateTaskAsync`** —
   <xref:ModelContextProtocol.Server.IMcpTaskStore.CreateTaskAsync*> must not return until the
   task is durably persisted, so that a subsequent
   <xref:ModelContextProtocol.Server.IMcpTaskStore.GetTaskAsync*> with the returned task ID
   resolves immediately — even from a different process or node. Stores backed by
   eventually-consistent storage must wait for the write to become visible (quorum
   acknowledgement, write-through, etc.) before returning. Required by SEP-2663 §306.
5. **Singleton under stateless HTTP** — when the server runs in stateless mode (each request
   spins up a fresh server instance), the same `IMcpTaskStore` instance must be shared across
   requests — either by registering it as a singleton in DI, or by backing it with external
   storage that every instance can reach. Otherwise `tasks/get` polls from subsequent requests
   will see an empty in-memory store and never find the task.

```csharp
public sealed class MyTaskStore : IMcpTaskStore
{
    public event Action<InputResponseReceivedEventArgs>? InputResponseReceived;

    public async Task ResolveInputRequestsAsync(
        string taskId,
        IDictionary<string, InputResponse> inputResponses,
        CancellationToken cancellationToken = default)
    {
        // 1. Atomically persist the resolved requests, ignoring keys that are no longer
        //    outstanding or that target a terminal task.
        await PersistResolvedResponsesAsync(taskId, inputResponses, cancellationToken);

        // 2. Then notify subscribers so any awaiting server.ElicitAsync/SampleAsync resumes.
        foreach (var kvp in inputResponses)
        {
            InputResponseReceived?.Invoke(new InputResponseReceivedEventArgs
            {
                TaskId = taskId,
                RequestId = kvp.Key,
                Response = kvp.Value,
            });
        }
    }

    // … other IMcpTaskStore members
}
```

### Status semantics

<xref:ModelContextProtocol.Protocol.McpTaskStatus.Completed> is the terminal status whenever the
underlying request produced its standard result, *including a
<xref:ModelContextProtocol.Protocol.CallToolResult> with `IsError = true`*. Per SEP-2663,
tool-level error results are not promoted to `Failed`.

<xref:ModelContextProtocol.Protocol.McpTaskStatus.Failed> is reserved for JSON-RPC protocol-level
errors during execution — for example, a malformed request, or an unhandled exception in a custom
handler that the SDK converts to a JSON-RPC error. Use
<xref:ModelContextProtocol.Protocol.CallToolResult.IsError?displayProperty=nameWithType> for
domain-level errors the model should see.

### Cancellation semantics

Per SEP-2663, `tasks/cancel` is **eventually consistent and cooperative**: the server acknowledges
the request immediately, but is not required to actually stop the work or to transition to
`Cancelled`. The notifications-cancelled mechanism (used for plain JSON-RPC requests) is not used
for task cancellation; clients must use `tasks/cancel`.

In the built-in SDK pipeline, when a task is wrapped by a configured `TaskStore`:

1. The store's `SetCancelledAsync` transitions the task to `Cancelled` (a no-op if the task is
   already terminal).
2. The associated `CancellationTokenSource` is signaled, propagating cancellation to the tool's
   `CancellationToken` so cooperative cleanup can run.
3. Whichever side (the cancel handler or the background runner's `finally` block) wins
   `TryRemove` on the cancellation source owns disposal, avoiding `ObjectDisposedException`.

### Architecture notes

#### Immutable store design

<xref:ModelContextProtocol.Server.InMemoryMcpTaskStore> uses immutable record snapshots with
compare-and-swap updates for lock-free thread safety. `InputRequests` and `InputResponses` are
exposed as `ImmutableDictionary<,>` so observers cannot mutate internal state.

#### Capability bypass inside a task scope

When `server.ElicitAsync`/`server.SampleAsync`/`server.RequestRootsAsync` execute inside a task
scope, the SDK intentionally skips the normal client-capability negotiation checks
(`ThrowIfElicitationUnsupported`, etc.). The tasks extension itself is the negotiated capability:
the client opted in by including the extension marker in the originating request, so it is
responsible for handling — or rejecting — the input requests surfaced through `tasks/get`.

### Known limitations

- **Server-push task status notifications (SEP-2575)**: not yet implemented. Clients rely on
  polling exclusively.
- **Lazy task creation**: when a tool runs through `TaskStore`, the store's
  <xref:ModelContextProtocol.Server.IMcpTaskStore.CreateTaskAsync*> is invoked eagerly before
  the inner handler runs, so tools that complete inline still incur a store write. There is
  currently no built-in deferral.
- **Mid-execution promotion to task**: an `[McpServerTool]` method cannot start executing
  synchronously and then transition its remaining work to a background task. Use a custom
  <xref:ModelContextProtocol.Server.McpServerHandlers.CallToolWithTaskHandler?displayProperty=nameWithType>
  if you need that pattern.
- **`roots/list` as an input request**: the server SDK routes `RequestRootsAsync` through the
  task channel when called from inside a task scope, but the client SDK does not currently
  dispatch a handler for that method. Avoid calling `server.RequestRootsAsync` from within a
  task scope until client-side support is added.
- **`ServerCapabilities.Extensions` round-trip**: the dictionary is typed as
  `IDictionary<string, object>` so its values cannot be deserialized by the source generator.
  The negotiated extension surfaces correctly at the wire level, but round-tripping arbitrary
  extension payloads in-process is not supported.
