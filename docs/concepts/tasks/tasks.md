---
title: Tasks
description: Run long-running tool invocations asynchronously with status polling and input requests.
uid: tasks
---

## Tasks

Tasks let an MCP server run a request asynchronously and report its result to the client later. The
primary use case today is long-running tool invocations: the tool is offloaded to a background task,
and the client polls for status, optionally exchanging additional input along the way.

Tasks are provided by the `ModelContextProtocol.Extensions.Tasks` package and require MCP protocol
version `2026-07-28` or later. The implementation follows
[SEP-2663 (Tasks Extension)](https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/seps/2663-tasks-extension.md).

### Overview

A client opts into tasks on a per-request basis by including the `io.modelcontextprotocol/tasks`
extension key in the request's `_meta`. When that opt-in is present, the server **might** respond
with a <xref:ModelContextProtocol.Extensions.Tasks.CreateTaskResult> instead of the standard result
(for example, <xref:ModelContextProtocol.Protocol.CallToolResult>). The client then polls `tasks/get`
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

<xref:ModelContextProtocol.Extensions.Tasks.McpTaskStatus> wire values are serialized in snake_case:
`working`, `input_required`, `completed`, `cancelled`, `failed`.

The discriminator field <xref:ModelContextProtocol.Protocol.Result.ResultType?displayProperty=nameWithType>
on the response payload is `"task"` for <xref:ModelContextProtocol.Extensions.Tasks.CreateTaskResult>
and `"complete"` for ordinary results.

### Server configuration

#### Using the task store

The easiest way to enable the package's task store integration is to call
<xref:ModelContextProtocol.Extensions.Tasks.McpTasksBuilderExtensions.WithTasks*> on the server builder,
passing an <xref:ModelContextProtocol.Extensions.Tasks.IMcpTaskStore>.
The SDK ships <xref:ModelContextProtocol.Extensions.Tasks.InMemoryMcpTaskStore> for development and tests:

```csharp
using ModelContextProtocol.Extensions.Tasks;

builder.Services.AddMcpServer()
    .WithTools<MyTools>()
    .WithTasks(new InMemoryMcpTaskStore());
```

When tasks are enabled with `WithTasks` the SDK automatically:

- Wires the `tasks/get`, `tasks/update`, and `tasks/cancel` handlers from the store.
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

Alternate-result `tools/call` filters run in registration order, with the Tasks filter creating a task at its position in that order. Filters before Tasks run before task creation. Filters after Tasks run in the background before the ordinary filter pipeline. ASP.NET Core tool authorization uses an alternate-result filter registered before Tasks, so an unauthorized call does not create a task.

Ordinary `tools/call` filters still run exactly once for task-backed calls. They execute in the background after the task record is created and before the tool body, so validation and telemetry continue to apply. Each background invocation gets an independent DI scope that remains alive until the tool pipeline completes.

For production scenarios that need durability, session isolation, multi-process routing, or
TTL-based cleanup, implement <xref:ModelContextProtocol.Extensions.Tasks.IMcpTaskStore> yourself
(see the [Implementing a custom task store](#implementing-a-custom-task-store) section).

#### Returning a task from a tool handler

For full control without the store's auto-wrapping, set
<xref:ModelContextProtocol.Server.McpServerHandlers.CallToolWithAlternateHandler?displayProperty=nameWithType>.
It returns a <xref:ModelContextProtocol.Protocol.ResultOrAlternate`1>, so each invocation can choose
between an immediate result and an alternate result such as a
<xref:ModelContextProtocol.Extensions.Tasks.CreateTaskResult>:

```csharp
using ModelContextProtocol.Extensions.Tasks;

options.Handlers.CallToolWithAlternateHandler = async (context, ct) =>
{
    if (ShouldRunInline(context.Params!))
    {
        return new CallToolResult { Content = [/* … */] };
    }

    var taskId = await StartBackgroundWorkAsync(context.Params!, ct);
    var created = new CreateTaskResult
    {
        TaskId = taskId,
        Status = McpTaskStatus.Working,
        CreatedAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow,
        PollIntervalMs = 1000,
    };

    return new ResultOrAlternate<CallToolResult>(created, McpTasksJsonContext.Default.CreateTaskResult);
};
```

> This low-level handler is mutually exclusive with `WithTasks`. When a store is configured, the
> SDK does the wrapping for you and throws `InvalidOperationException` if the alternate handler also
> returns an alternate. Use one mechanism or the other. When you return a task this way, you're also
> responsible for serving `tasks/get`, `tasks/update`, and `tasks/cancel`, which the store provides
> automatically.

> <xref:ModelContextProtocol.Server.McpServerHandlers.CallToolHandler?displayProperty=nameWithType>
> and <xref:ModelContextProtocol.Server.McpServerHandlers.CallToolWithAlternateHandler?displayProperty=nameWithType>
> are mutually exclusive. Setting one while the other is already non-null throws
> `InvalidOperationException` at the property setter.

### Client usage

#### Automatic polling

<xref:ModelContextProtocol.Extensions.Tasks.McpTasksClientExtensions.CallToolWithPollingAsync*>
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
using ModelContextProtocol.Extensions.Tasks;

var result = await client.CallToolWithPollingAsync(
    new CallToolRequestParams { Name = "long-running-tool", Arguments = arguments },
    cancellationToken: cancellationToken);
```

#### Manual control

Use <xref:ModelContextProtocol.Extensions.Tasks.McpTasksClientExtensions.CallToolAsTaskAsync*> to receive the raw
<xref:ModelContextProtocol.Extensions.Tasks.ResultOrCreatedTask`1> without auto-polling, then drive the
lifecycle yourself using <xref:ModelContextProtocol.Extensions.Tasks.McpTasksClientExtensions.GetTaskAsync*>,
<xref:ModelContextProtocol.Extensions.Tasks.McpTasksClientExtensions.UpdateTaskAsync*>, and
<xref:ModelContextProtocol.Extensions.Tasks.McpTasksClientExtensions.CancelTaskAsync*>:

```csharp
using ModelContextProtocol.Extensions.Tasks;

var raw = await client.CallToolAsTaskAsync(requestParams, cancellationToken);
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

`CallToolWithPollingAsync` includes a safety net for misbehaving servers: if the task stays in
<xref:ModelContextProtocol.Extensions.Tasks.McpTaskStatus.InputRequired> across many consecutive polls
without exposing any new input request keys (that is, every previously requested input has already
been resolved by the client and yet the server keeps returning `InputRequired`), the client
gives up, issues a best-effort `tasks/cancel`, and throws
<xref:ModelContextProtocol.McpException>. This guards against a server that never transitions
out of `InputRequired` and prevents an unbounded poll loop.

The threshold defaults to `60` consecutive stuck polls and is configurable via the
`maxConsecutiveStuckPolls` parameter on `CallToolWithPollingAsync`. The effective
wall-clock timeout is roughly `maxConsecutiveStuckPolls * pollIntervalMs`, so tune the value
with the server-side poll cadence in mind. Setting it too low risks false positives for servers
that are slow to surface follow-up input requests; setting it too high can mask misbehaving
servers.

### Input requests (multi-round-trip)

When a task needs additional input from the client, the server transitions it to
<xref:ModelContextProtocol.Extensions.Tasks.McpTaskStatus.InputRequired> and returns the outstanding
requests in <xref:ModelContextProtocol.Extensions.Tasks.InputRequiredTaskResult.InputRequests>. Each
entry is an arbitrary key paired with a `{ method, params }` envelope representing an
equivalent standalone server-to-client request. The client provides answers via
<xref:ModelContextProtocol.Extensions.Tasks.McpTasksClientExtensions.UpdateTaskAsync*>, keyed by the same identifiers.

Supported input request methods:

| Method | Dispatched to the client handler |
| --- | --- |
| `elicitation/create` | <xref:ModelContextProtocol.Client.McpClientHandlers.ElicitationHandler> |
| `sampling/createMessage` | <xref:ModelContextProtocol.Client.McpClientHandlers.SamplingHandler> |

Per SEP-2663:

- Each input request key **must** be unique over the lifetime of the task.
- Clients **should** deduplicate keys across polls so a request is only presented to the user
  or model once. `CallToolWithPollingAsync` does this automatically.
- Servers **should** ignore `inputResponses` entries whose key does not currently correspond to
  an outstanding request, including responses for terminal-state tasks.
  <xref:ModelContextProtocol.Extensions.Tasks.InMemoryMcpTaskStore> follows this rule.

### Implementing a custom task store

Implement <xref:ModelContextProtocol.Extensions.Tasks.IMcpTaskStore> for production scenarios. Key
requirements drawn from the SEP and the SDK contract:

1. **Thread safety** — every method can be called concurrently.
2. **Idempotent terminal transitions** —
   <xref:ModelContextProtocol.Extensions.Tasks.IMcpTaskStore.SetCompletedAsync*>,
   <xref:ModelContextProtocol.Extensions.Tasks.IMcpTaskStore.SetFailedAsync*>, and
   <xref:ModelContextProtocol.Extensions.Tasks.IMcpTaskStore.SetCancelledAsync*> must be no-ops on a task
   that is already in a terminal state so a late cancellation cannot overwrite a result.
3. **`InputResponseReceived` event** — after persisting an input response inside
   <xref:ModelContextProtocol.Extensions.Tasks.IMcpTaskStore.ResolveInputRequestsAsync*>, raise
   <xref:ModelContextProtocol.Extensions.Tasks.IMcpTaskStore.InputResponseReceived?displayProperty=nameWithType>
   for each resolved entry. This is the only mechanism that wakes a pending
   `server.ElicitAsync`/`server.SampleAsync` call waiting inside a task scope. In distributed
   deployments where a different server instance receives the `tasks/update`, the event must
   be propagated to the originating server (for example via Redis pub/sub, SignalR, or a custom
   transport).
4. **Strong-consistency on `CreateTaskAsync`** —
   <xref:ModelContextProtocol.Extensions.Tasks.IMcpTaskStore.CreateTaskAsync*> must not return until the
   task is durably persisted, so that a subsequent
   <xref:ModelContextProtocol.Extensions.Tasks.IMcpTaskStore.GetTaskAsync*> with the returned task ID
   resolves immediately — even from a different process or node. Stores backed by
   eventually consistent storage must wait for the write to become visible (quorum
   acknowledgement, write-through, etc.) before returning. Required by SEP-2663 §306.
   When the call includes a non-null `executionIntent`, persist it atomically with the task
   record so it can be read back via
   <xref:ModelContextProtocol.Extensions.Tasks.McpTaskInfo.ExecutionIntent*>; copy the
   `JsonElement` (for example with `Clone()`) instead of retaining the executor's original
   backing document — the executor may dispose that document once execution is handed off,
   and a retained reference surfaces later as an `ObjectDisposedException`. The intent is
   server-only — never surfaced in protocol responses, notifications, or errors — and a
   store that cannot persist it must throw rather than silently dropping it.
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

### Delegating execution to an external runtime

By default, `WithTasks` executes the tool in-process on the .NET thread pool. To delegate
execution to a durable system such as Temporal, Orleans, Hangfire, or an external queue,
register an <xref:ModelContextProtocol.Extensions.Tasks.IMcpTaskExecutor>:

```csharp
builder.WithTasks(
    myDurableTaskStore,
    options =>
    {
        options.TaskExecutor = new TemporalTaskExecutor(workflowClient);
    });
```

An executor can also be resolved from the service provider — register `IMcpTaskExecutor` in DI
and omit `TaskExecutor`. The executor is resolved from each task's execution scope, so scoped
registrations get one instance per task; singleton registrations behave as usual. When neither
is configured, tasks run in-process exactly as before.

The executor's <xref:ModelContextProtocol.Extensions.Tasks.IMcpTaskExecutor.StartAsync*> method
is invoked after the task record is durably created in the store, and must return only
after execution has been durably started — for example, after the external runtime has
accepted the job — mirroring the durability requirement SEP-2663 §306 places on
<xref:ModelContextProtocol.Extensions.Tasks.IMcpTaskStore.CreateTaskAsync*>. It must not wait
for the task to complete. If `StartAsync` throws, the exception is not returned as an error
from the original `tools/call`: that call still succeeds with
<xref:ModelContextProtocol.Extensions.Tasks.CreateTaskResult>, the task is marked failed via
`SetFailedAsync`, and the client discovers the failure on its first `tasks/get` poll. By
contrast, failures before the task record exists — resolving the executor, creating the
execution intent, or
<xref:ModelContextProtocol.Extensions.Tasks.IMcpTaskStore.CreateTaskAsync*> — fail the
original `tools/call` with an error result, and nothing is persisted: no task record is left
orphaned at `Working`, and no intent exists without its task.
After a successful `StartAsync`, the SDK stops tracking the task and the store is the single
source of truth for its state.

#### Persisted execution intent

There is still a crash window between `CreateTaskAsync` completing and `StartAsync` returning:
if the process exits during it, the store is left with a `Working` task whose work was never
durably submitted to the external runtime. To close that window, executors that delegate to an
external runtime also implement
<xref:ModelContextProtocol.Extensions.Tasks.IMcpTaskExecutor.CreateExecutionIntentAsync*>.
The full ordering is:

1. Authorization and validation (the filters registered before Tasks) run.
2. The SDK calls `CreateExecutionIntentAsync`, which returns a portable, side-effect-free
   description of how the task will be started — or `null` for stateless executors like
   <xref:ModelContextProtocol.Extensions.Tasks.ProcessLocalMcpTaskExecutor>, which run tools
   in-process and have nothing to reconstruct.
3. The SDK passes the intent to
   <xref:ModelContextProtocol.Extensions.Tasks.IMcpTaskStore.CreateTaskAsync*>, which persists
   the task record and the intent atomically, as a single write.
4. The SDK calls `StartAsync` to start the external work.

The intent contract:

- **Opaque and executor-owned.** Its schema and versioning belong to the executor, not to the
  SDK or the protocol. An intent can outlive the process that wrote it — an orphan recovered
  at startup may have been persisted by an earlier deployment of the executor — so version
  the payload (for example with a `version` field) so reconciliation code can tell
  generations apart and evolve safely.
- **Portable.** Only data the external runtime needs to reconstruct the submission — no
  runtime objects, services, credentials, clients, transports, or delegates. The intent is
  persisted in the task store alongside the task record, so treat it like any other durable
  data: anything with store access can read it, and secrets must stay out of it.
- **Server-only.** It never surfaces in MCP responses, notifications, or errors; it is
  readable only through the store's
  <xref:ModelContextProtocol.Extensions.Tasks.McpTaskInfo.ExecutionIntent*> property (and
  <xref:ModelContextProtocol.Extensions.Tasks.McpTaskExecutionContext.ExecutionIntent*> for
  the task's executor).
- **Copy and reject in the store.** Stores must copy the `JsonElement` (for example with
  `Clone()`) rather than retaining a reference to the executor's backing document — the
  executor may dispose that document once execution is handed off, and a retained reference
  surfaces later as an `ObjectDisposedException`. A store that cannot persist a non-null
  intent must throw, rejecting the task creation, rather than silently dropping it.

The intent captures the *submission*, not the execution. The DI execution scope and its
request-scoped services, the matched tool primitive, the context token wired to
`tasks/cancel`, and the task's input-request channel for elicitation and sampling all die
with the crashed process. A recovered execution therefore behaves like any other external
worker: it runs in the external runtime, records progress and results in the store, and has
no <xref:ModelContextProtocol.Extensions.Tasks.McpTaskExecutionContext.RunToolPipelineAsync*>
to fall back on. Cancellation for a recovered task cannot reach the dead process's token, so
it must be propagated through the external runtime or the store, as with any cross-process
execution; multi-round-trip input relies on the store's
<xref:ModelContextProtocol.Extensions.Tasks.IMcpTaskStore.InputResponseReceived?displayProperty=nameWithType>
event.

```csharp
public sealed class TemporalTaskExecutor(ITemporalClient workflowClient) : IMcpTaskExecutor
{
    public ValueTask<JsonElement?> CreateExecutionIntentAsync(
        RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
    {
        // Portable, versioned data only: what the workflow needs to reconstruct the
        // submission. No services, clients, transports, credentials, or delegates —
        // this element must survive a process restart inside the task store.
        JsonObject intent = new()
        {
            ["version"] = 1,
            ["request"] = JsonSerializer.SerializeToNode(
                request.Params,
                McpJsonUtilities.DefaultOptions.GetTypeInfo<CallToolRequestParams>()),
        };

        return ValueTask.FromResult<JsonElement?>(
            JsonSerializer.SerializeToElement(
                intent,
                McpJsonUtilities.DefaultOptions.GetTypeInfo<JsonNode>()));
    }

    public async ValueTask StartAsync(
        McpTaskExecutionContext context, CancellationToken cancellationToken)
    {
        // Submit the tool request to the durable runtime. The workflow communicates with
        // IMcpTaskStore directly to record progress and results.
        await workflowClient.StartWorkflowAsync(
            "run-mcp-task",
            new McpTaskPayload(context.TaskId, context.Request.Params),
            id: context.TaskId,
            cancellationToken);

        // The scope-bound services are no longer needed in this process.
        await context.DisposeAsync();
    }
}
```

#### Recovering orphaned tasks

Because the task record and its intent are persisted together, a restarting integration can
discover orphaned `Working` tasks through its own durable store, read the persisted intent,
and reconstruct the submission. Reconciliation must be safe at either crash point: if the
external runtime never accepted the job, resubmission starts it; if the runtime *had*
accepted the job before the crash, resubmitting with the same task ID deduplicates — most
external runtimes treat the ID as a unique workflow or job key. Either way, re-read the
task's state immediately before resubmitting and leave tasks that already reached a terminal
state — completed, failed, or cancelled by a client while the process was down — untouched.

Until reconciliation runs, clients polling an orphan observe `Working` for as long as the
task's TTL permits; reconciliation is what moves the orphan to a terminal state. An orphan
can also linger in
<xref:ModelContextProtocol.Extensions.Tasks.McpTaskStatus.InputRequired> when the process
died later in execution — the same intent-based reconstruction applies, but the pending
input exchange additionally needs a live `InputResponseReceived` subscriber before it can
resume.

The SDK performs no reconciliation itself; this is integration code, typically run at
startup. <xref:ModelContextProtocol.Extensions.Tasks.InMemoryMcpTaskStore> retains the
intent alongside the task record, but like all of its state it does not survive process
restarts — intent-based recovery requires a store backed by durable storage. For tasks
created without an intent (a stateless executor was configured), reconciliation cannot
reconstruct a submission, so integrations fall back to their own strategy — TTL cleanup,
for example.

```csharp
foreach (var task in await durableStore.FindWorkingTasksAsync())
{
    if (task.ExecutionIntent is not { } intent)
    {
        continue; // Created by a stateless executor; nothing to reconstruct.
    }

    var payload = JsonNode.Parse(intent.GetRawText())!;
    if (payload["version"]?.GetValue<int>() is not 1)
    {
        continue; // Unknown intent generation: skip or migrate explicitly.
    }

    var requestParams = payload["request"]!.Deserialize(
        McpJsonUtilities.DefaultOptions.GetTypeInfo<CallToolRequestParams>())!;

    // Re-check the task's state if discovery ran earlier — terminal tasks stay
    // untouched — then resubmit. The workflow ID is the task ID: a deduplicated
    // no-op if the job was already accepted before the crash, a reconstruction
    // if it never was.
    await workflowClient.StartWorkflowAsync(
        "run-mcp-task",
        new McpTaskPayload(task.TaskId, requestParams),
        id: task.TaskId,
        cancellationToken);
}
```

The <xref:ModelContextProtocol.Extensions.Tasks.McpTaskExecutionContext> passed to the
executor exposes the task identity, the matched tool request bound to a fresh execution
scope, and a token that fires on `tasks/cancel`. Executors that want the tool to run
locally call
<xref:ModelContextProtocol.Extensions.Tasks.McpTaskExecutionContext.RunToolPipelineAsync*>,
which runs the remaining request filters and the tool, records the outcome in the store,
and releases the execution scope. Executors that hand execution off to an external system
should read what they need from
<xref:ModelContextProtocol.Extensions.Tasks.McpTaskExecutionContext.Request*> and then call
<xref:ModelContextProtocol.Extensions.Tasks.McpTaskExecutionContext.DisposeAsync*> to release
the scope-bound services.

Primitive matching and the filters registered before Tasks — including ASP.NET Core
authorization — have already run by the time `StartAsync` is called. The remaining
alternate-result filters and the ordinary call-tool filters run only inside
`RunToolPipelineAsync`, so an executor that performs a pure handoff to an external runtime
bypasses them. When the tool pipeline will not run locally, validation, auditing,
transformations, and other cross-cutting policies must be applied by the external runtime —
or by a filter registered before Tasks — instead.

`tasks/get`, `tasks/update`, and `tasks/cancel` continue to be served entirely from the
`IMcpTaskStore`, so a different server instance can serve polling clients after the process
that started the task exits — the acceptance scenario for durable execution.

Note that <xref:ModelContextProtocol.Extensions.Tasks.McpTaskExecutionContext.CancellationToken*>
signals cancellation only within the process that created the task. A `tasks/cancel` handled
by a different server instance can update the shared store, but it cannot signal that
process's token. External-runtime integrations whose cancellation must survive server
replacement therefore need to propagate it through their store or another durable mechanism;
the token remains the cancellation signal for the default in-process executor and other
same-process execution paths.

Note that elicitation and sampling issued from *outside* the process that owns the client
session cannot be routed through the task's input-request channel; an external worker that
needs multi-round-trip input should rely on the store's
<xref:ModelContextProtocol.Extensions.Tasks.IMcpTaskStore.InputResponseReceived?displayProperty=nameWithType>
event, or run the pipeline locally via
<xref:ModelContextProtocol.Extensions.Tasks.McpTaskExecutionContext.RunToolPipelineAsync*>
from the process that owns the session.

### Status semantics

<xref:ModelContextProtocol.Extensions.Tasks.McpTaskStatus.Completed> is the terminal status whenever the
underlying request produced its standard result, *including a
<xref:ModelContextProtocol.Protocol.CallToolResult> with `IsError = true`*. Per SEP-2663,
tool-level error results are not promoted to `Failed`.

<xref:ModelContextProtocol.Extensions.Tasks.McpTaskStatus.Failed> is reserved for JSON-RPC protocol-level
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

<xref:ModelContextProtocol.Extensions.Tasks.InMemoryMcpTaskStore> uses immutable record snapshots with
compare-and-swap updates for lock-free thread safety. `InputRequests` and `InputResponses` are
exposed as `ImmutableDictionary<,>` so observers can't mutate internal state.

#### Capability bypass inside a task scope

When `server.ElicitAsync`/`server.SampleAsync`/`server.RequestRootsAsync` execute inside a task
scope, the SDK intentionally skips the normal client-capability negotiation checks
(`ThrowIfElicitationUnsupported`, etc.). The tasks extension itself is the negotiated capability:
the client opted in by including the extension marker in the originating request, so it's
responsible for handling — or rejecting — the input requests surfaced through `tasks/get`.

#### Compatibility with v1 experimental Tasks

The Tasks extension in v2.0.0 replaces the experimental Tasks implementation shipped in v1.3.0 and
v1.4.x. The implementations are not compatible at either the API or protocol level. A v2 Tasks
client or server must use a connection negotiated to `2026-07-28` or later; it cannot fall back to
the down-level implementation.

On a connection negotiated to the down-level `2025-11-25` protocol:

- A v2 client calling a v1 server receives an ordinary tool result. The v2 client does not opt in
  to the down-level Tasks protocol, and `GetTaskAsync` rejects use before a `2026-07-28`
  connection is negotiated.
- A v1 client calling a v2 server likewise receives an ordinary tool result. The v2 server does
  not create Tasks on a down-level connection, and its `tasks/get` endpoint rejects the legacy
  request with a method-not-found error.

Upgrade both peers to the v2 Tasks extension before using Tasks. The extension provides no
compatibility bridge for the previous experimental API.

### Known limitations

- **Server-push task status notifications (SEP-2575)**: not yet implemented. Clients rely on
  polling exclusively.
- **Orphaned-task reconciliation**: the SDK persists the executor's execution intent with the
  task record, but discovering orphaned tasks and resubmitting them is integration code; the
  SDK performs no reconciliation itself (see
  [Persisted execution intent](#persisted-execution-intent)).
- **Lazy task creation**: when a tool runs through the task store, the store's
  <xref:ModelContextProtocol.Extensions.Tasks.IMcpTaskStore.CreateTaskAsync*> is invoked eagerly before
  the inner handler runs, so tools that complete inline still incur a store write. There is
  currently no built-in deferral.
- **Mid-execution promotion to task**: an `[McpServerTool]` method cannot start executing
  synchronously and then transition its remaining work to a background task. Use a custom
  <xref:ModelContextProtocol.Server.McpServerHandlers.CallToolWithAlternateHandler?displayProperty=nameWithType>
  if you need that pattern.
- **`roots/list` as an input request**: the server SDK routes `RequestRootsAsync` through the
  task channel when called from inside a task scope, but the client SDK does not currently
  dispatch a handler for that method. Avoid calling `server.RequestRootsAsync` from within a
  task scope until client-side support is added.
- **`ServerCapabilities.Extensions` round-trip**: the dictionary is typed as
  `IDictionary<string, object>` so its values cannot be deserialized by the source generator.
  The negotiated extension surfaces correctly at the wire level, but round-tripping arbitrary
  extension payloads in-process is not supported.
