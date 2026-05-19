---
title: Multi Round-Trip Requests (MRTR)
author: halter73
description: How servers request client input during tool execution using Multi Round-Trip Requests.
uid: mrtr
---

# Multi Round-Trip Requests (MRTR)

<!-- mlc-disable-next-line -->
> [!WARNING]
> MRTR is an **experimental feature** based on a draft MCP specification proposal. The API may change in future releases. See the [Experimental APIs](../../experimental.md) documentation for details on working with experimental APIs. Both the client and server must opt in via <xref:ModelContextProtocol.Client.McpClientOptions.ExperimentalProtocolVersion> and <xref:ModelContextProtocol.Server.McpServerOptions.ExperimentalProtocolVersion> respectively.

Multi Round-Trip Requests (MRTR) allow a server tool to request input from the client — such as [elicitation](xref:elicitation), [sampling](xref:sampling), or [roots](xref:roots) — as part of a single tool call, without requiring a separate JSON-RPC request for each interaction. Instead of sending a final result, the server returns an **incomplete result** containing one or more input requests. The client fulfills those requests and retries the original tool call with the responses attached.

## Overview

MRTR is useful when:

- A tool needs user confirmation before proceeding (elicitation)
- A tool needs LLM reasoning from the client (sampling)
- A tool needs an updated list of client roots
- A tool needs to perform multiple rounds of interaction in a single logical operation
- A stateless server needs to orchestrate multi-step flows without keeping handler state in memory

## How MRTR works

1. The client calls a tool on the server via `tools/call`.
2. The server tool determines it needs client input and returns an `IncompleteResult` containing `inputRequests` and/or `requestState`.
3. The client resolves each input request (e.g., prompts the user for elicitation, calls an LLM for sampling).
4. The client retries the original `tools/call` with `inputResponses` (keyed to the input requests) and `requestState` echoed back.
5. The server processes the responses and either returns a final result or another `IncompleteResult` for additional rounds.

## Opting in

MRTR requires both the client and server to opt in by setting `ExperimentalProtocolVersion` to a draft protocol version. Currently, this is `"2026-06-XX"`:

```csharp
// Server
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMcpServer(options =>
{
    options.ExperimentalProtocolVersion = "2026-06-XX";
})
.WithTools<MyTools>();
```

```csharp
// Client
var options = new McpClientOptions
{
    ExperimentalProtocolVersion = "2026-06-XX",
    Handlers = new McpClientHandlers
    {
        ElicitationHandler = HandleElicitationAsync,
        SamplingHandler = HandleSamplingAsync,
    }
};
```

When both sides opt in, the negotiated protocol version activates MRTR. When either side does not opt in, the SDK gracefully falls back to standard behavior.

## High-level API

The high-level API lets tool handlers call <xref:ModelContextProtocol.Server.McpServer.ElicitAsync*> and <xref:ModelContextProtocol.Server.McpServer.SampleAsync*> as if they were simple async calls. The SDK transparently manages the incomplete result / retry cycle.

```csharp
[McpServerToolType]
public class InteractiveTools
{
    [McpServerTool, Description("Asks the user for confirmation before proceeding")]
    public static async Task<string> ConfirmAction(
        McpServer server,
        [Description("The action to confirm")] string action,
        CancellationToken cancellationToken)
    {
        var result = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = $"Do you want to proceed with: {action}?",
            RequestedSchema = new()
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    ["confirm"] = new ElicitRequestParams.BooleanSchema
                    {
                        Description = "Confirm the action"
                    }
                }
            }
        }, cancellationToken);

        return result.Action == "accept" ? "Action confirmed!" : "Action cancelled.";
    }
}
```

From the client's perspective, this is a single `CallToolAsync` call. The SDK handles all retries automatically:

```csharp
var result = await client.CallToolAsync("ConfirmAction", new { action = "delete all files" });
Console.WriteLine(result.Content.OfType<TextContentBlock>().First().Text);
```

> [!TIP]
> The high-level API requires session affinity — the handler task stays suspended in server memory between round trips. This works well for stateful (non-stateless) server configurations.

## Low-level API

The low-level API gives tool handlers direct control over `inputRequests` and `requestState`. This enables stateless multi-round-trip flows where the server does not need to keep handler state in memory between retries.

### Checking MRTR support

Before using the low-level API, check <xref:ModelContextProtocol.Server.McpServer.IsMrtrSupported> to determine if the connected client supports MRTR. If it does not, provide a fallback experience:

```csharp
[McpServerTool, Description("A tool that uses low-level MRTR")]
public static string MyTool(
    McpServer server,
    RequestContext<CallToolRequestParams> context)
{
    if (!server.IsMrtrSupported)
    {
        return "This tool requires a client that supports multi-round-trip requests. "
             + "Please upgrade your client or enable experimental protocol support.";
    }

    // ... MRTR logic
}
```

### Returning an incomplete result

Throw <xref:ModelContextProtocol.Protocol.IncompleteResultException> to return an incomplete result to the client. The exception carries an <xref:ModelContextProtocol.Protocol.IncompleteResult> containing `inputRequests` and/or `requestState`:

```csharp
[McpServerTool, Description("Stateless tool managing its own MRTR flow")]
public static string StatelessTool(
    McpServer server,
    RequestContext<CallToolRequestParams> context,
    [Description("The user's question")] string question)
{
    var requestState = context.Params!.RequestState;
    var inputResponses = context.Params!.InputResponses;

    // On retry, process the client's responses
    if (requestState is not null && inputResponses is not null)
    {
        var elicitResult = inputResponses["user_answer"].ElicitationResult;
        return $"You answered: {elicitResult?.Content?.FirstOrDefault().Value}";
    }

    if (!server.IsMrtrSupported)
    {
        return "MRTR is not supported by this client.";
    }

    // First call — request user input
    throw new IncompleteResultException(
        inputRequests: new Dictionary<string, InputRequest>
        {
            ["user_answer"] = InputRequest.ForElicitation(new ElicitRequestParams
            {
                Message = $"Please answer: {question}",
                RequestedSchema = new()
                {
                    Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                    {
                        ["answer"] = new ElicitRequestParams.StringSchema
                        {
                            Description = "Your answer"
                        }
                    }
                }
            })
        },
        requestState: "awaiting-answer");
}
```

### Accessing retry data

When the client retries a tool call, the retry data is available on the request parameters:

- <xref:ModelContextProtocol.Protocol.RequestParams.InputResponses> — a dictionary of client responses keyed by the same keys used in `inputRequests`
- <xref:ModelContextProtocol.Protocol.RequestParams.RequestState> — the opaque state string echoed back by the client

Each `InputResponse` has typed accessors for the response type:

- `ElicitationResult` — the result of an elicitation request
- `SamplingResult` — the result of a sampling request
- `RootsResult` — the result of a roots list request

### Load shedding with requestState-only responses

A server can return a `requestState`-only incomplete result (without any `inputRequests`) to defer processing. This is useful for load shedding or breaking up long-running work across multiple requests:

```csharp
[McpServerTool, Description("Tool that defers work using requestState")]
public static string DeferredTool(
    McpServer server,
    RequestContext<CallToolRequestParams> context)
{
    var requestState = context.Params!.RequestState;

    if (requestState is not null)
    {
        // Resume deferred work
        var state = JsonSerializer.Deserialize<MyState>(
            Convert.FromBase64String(requestState));
        return $"Completed step {state!.Step}";
    }

    if (!server.IsMrtrSupported)
    {
        return "MRTR is not supported by this client.";
    }

    // Defer work to a later retry
    var initialState = new MyState { Step = 1 };
    throw new IncompleteResultException(
        requestState: Convert.ToBase64String(
            JsonSerializer.SerializeToUtf8Bytes(initialState)));
}
```

The client automatically retries `requestState`-only incomplete results, echoing the state back without needing to resolve any input requests.

### Multiple round trips

A tool can perform multiple rounds of interaction by throwing `IncompleteResultException` multiple times across retries:

```csharp
[McpServerTool, Description("Multi-step wizard")]
public static string WizardTool(
    McpServer server,
    RequestContext<CallToolRequestParams> context)
{
    var requestState = context.Params!.RequestState;
    var inputResponses = context.Params!.InputResponses;

    if (requestState == "step-2" && inputResponses is not null)
    {
        var name = inputResponses["name"].ElicitationResult?.Content?.FirstOrDefault().Value;
        var age = inputResponses["age"].ElicitationResult?.Content?.FirstOrDefault().Value;
        return $"Welcome, {name}! You are {age} years old.";
    }

    if (requestState == "step-1" && inputResponses is not null)
    {
        var name = inputResponses["name"].ElicitationResult?.Content?.FirstOrDefault().Value;

        // Second round — ask for age
        throw new IncompleteResultException(
            inputRequests: new Dictionary<string, InputRequest>
            {
                ["age"] = InputRequest.ForElicitation(new ElicitRequestParams
                {
                    Message = $"Hi {name}! How old are you?",
                    RequestedSchema = new()
                    {
                        Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                        {
                            ["age"] = new ElicitRequestParams.NumberSchema
                            {
                                Description = "Your age"
                            }
                        }
                    }
                })
            },
            requestState: "step-2");
    }

    if (!server.IsMrtrSupported)
    {
        return "MRTR is not supported. Please use a compatible client.";
    }

    // First round — ask for name
    throw new IncompleteResultException(
        inputRequests: new Dictionary<string, InputRequest>
        {
            ["name"] = InputRequest.ForElicitation(new ElicitRequestParams
            {
                Message = "What's your name?",
                RequestedSchema = new()
                {
                    Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                    {
                        ["name"] = new ElicitRequestParams.StringSchema
                        {
                            Description = "Your name"
                        }
                    }
                }
            })
        },
        requestState: "step-1");
}
```

### Providing custom error messages

When MRTR is not supported, you can provide domain-specific guidance:

```csharp
if (!server.IsMrtrSupported)
{
    return "This tool requires interactive input, but your client doesn't support "
         + "multi-round-trip requests. To use this feature:\n"
         + "1. Update to a client that supports MCP protocol version 2026-06-XX or later\n"
         + "2. Enable the experimental protocol version in your client configuration\n"
         + "\nFor more information, see: https://example.com/mrtr-setup";
}
```

## Compatibility

The SDK handles all four combinations of experimental/non-experimental client and server:

| Server Experimental | Client Experimental | Behavior |
|---|---|---|
| ✅ | ✅ | MRTR — incomplete results with retry cycle |
| ✅ | ❌ | Server falls back to legacy JSON-RPC requests for elicitation/sampling |
| ❌ | ✅ | Client accepts stable protocol version; MRTR retry loop is a no-op |
| ❌ | ❌ | Standard behavior — no MRTR |

When a server has MRTR enabled but the connected client does not:

- The high-level API (`ElicitAsync`, `SampleAsync`) automatically falls back to sending standard JSON-RPC requests — no code changes needed.
- The low-level API reports `IsMrtrSupported == false`, allowing the tool to provide a custom fallback message.

### Backward compatibility for MRTR-native tools

Tools written with the low-level MRTR pattern (`IncompleteResultException`) work automatically with clients that don't support MRTR. When a tool throws `IncompleteResultException` and the client hasn't negotiated MRTR, the SDK resolves each `InputRequest` by sending the corresponding standard JSON-RPC call (elicitation, sampling, or roots) to the client, then retries the handler with the resolved responses.

This means you can write a single tool implementation using the MRTR-native pattern and it will work with any client:

```csharp
[McpServerTool, Description("Get weather with user's preferred units")]
public static string GetWeather(
    RequestContext<CallToolRequestParams> context,
    string location)
{
    // On retry, inputResponses and requestState are populated
    if (context.Params!.InputResponses?.TryGetValue("units", out var response) == true)
    {
        var units = response.ElicitationResult?.Content?.FirstOrDefault().Value;
        return $"Weather for {location} in {units}: 72°";
    }

    // First call: request the user's preferred units
    throw new IncompleteResultException(
        inputRequests: new Dictionary<string, InputRequest>
        {
            ["units"] = InputRequest.ForElicitation(new ElicitRequestParams
            {
                Message = "Which temperature units?",
                RequestedSchema = new()
            })
        },
        requestState: "awaiting-units");
}
```

- **With an MRTR client**: The `IncompleteResult` is sent over the wire. The client resolves the elicitation and retries with `inputResponses`.
- **Without MRTR**: The SDK sends a standard `elicitation/create` JSON-RPC request to the client, collects the response, and retries the handler internally. The client never sees the `IncompleteResult`.

> [!NOTE]
> The backcompat retry loop resolves up to 10 rounds. Tools that need more rounds should use the high-level API (`ElicitAsync`) instead.

## Transitioning from MRTR to Tasks

<!-- mlc-disable-next-line -->
> [!WARNING]
> Deferred task creation depends on both the [MRTR](xref:mrtr) and [Tasks](xref:tasks) experimental features.

Some tools need user input before they can decide whether to start a long-running background task. For example, a VM provisioning tool might confirm costs with the user before committing to a task that takes minutes. **Deferred task creation** lets a tool perform ephemeral MRTR exchanges first, then transition to a background task only when ready.

### How it works

1. The tool sets `DeferTaskCreation = true` on its attribute or options.
2. When the client sends task metadata with the `tools/call` request, the SDK runs the tool through the normal MRTR-wrapped path instead of creating a task immediately.
3. The tool calls `ElicitAsync` or `SampleAsync` as usual — these use MRTR (incomplete result / retry cycles).
4. When the tool is ready, it calls `await server.CreateTaskAsync(cancellationToken)` to transition to a background task.
5. After `CreateTaskAsync`, the MRTR phase ends. Any subsequent `ElicitAsync` or `SampleAsync` calls use the task's own `input_required` / `tasks/input_response` mechanism instead.
6. If the tool returns without calling `CreateTaskAsync`, a normal (non-task) result is sent to the client.

### Server example

```csharp
McpServerTool.Create(
    async (string vmName, McpServer server, CancellationToken ct) =>
    {
        // Phase 1: Ephemeral MRTR — confirm with user before starting expensive work.
        var confirmation = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = $"Provision VM '{vmName}'? This will incur costs.",
            RequestedSchema = new()
        }, ct);

        if (confirmation.Action != "confirm")
        {
            return "Cancelled by user.";
        }

        // Phase 2: Transition to a background task.
        await server.CreateTaskAsync(ct);

        // Phase 3: Background work — runs as a task, client polls for status.
        await Task.Delay(TimeSpan.FromMinutes(5), ct);
        return $"VM '{vmName}' provisioned successfully.";
    },
    new McpServerToolCreateOptions
    {
        Name = "provision-vm",
        Description = "Provisions a VM with user confirmation",
        DeferTaskCreation = true,
        Execution = new ToolExecution { TaskSupport = ToolTaskSupport.Optional },
    })
```

The attribute-based equivalent uses `DeferTaskCreation` on <xref:ModelContextProtocol.Server.McpServerToolAttribute>:

```csharp
[McpServerTool(DeferTaskCreation = true, TaskSupport = ToolTaskSupport.Optional)]
[Description("Provisions a VM with user confirmation")]
public static async Task<string> ProvisionVm(
    string vmName, McpServer server, CancellationToken ct)
{
    var confirmation = await server.ElicitAsync(new ElicitRequestParams
    {
        Message = $"Provision VM '{vmName}'? This will incur costs.",
        RequestedSchema = new()
    }, ct);

    if (confirmation.Action != "confirm")
        return "Cancelled by user.";

    await server.CreateTaskAsync(ct);

    await Task.Delay(TimeSpan.FromMinutes(5), ct);
    return $"VM '{vmName}' provisioned successfully.";
}
```

### Key points

- **One-way transition**: Once `CreateTaskAsync` is called, the tool cannot go back to ephemeral MRTR. All subsequent input requests use the task workflow.
- **Optional task creation**: A `DeferTaskCreation` tool can return a normal result without ever calling `CreateTaskAsync`. The tool decides at runtime whether to create a task.
- **No task metadata, no deferral**: If the client calls the tool without task metadata, the tool runs normally with MRTR — `DeferTaskCreation` has no effect.

For more details on task configuration and lifecycle, see the [Tasks](xref:tasks) documentation.

## Choosing between high-level and low-level APIs

| Consideration | High-level API | Low-level API |
|---|---|---|
| **Session affinity** | Required — handler stays suspended in memory | Not required — handler completes each round |
| **State management** | Automatic (SDK manages via `MrtrContext`) | Manual (`requestState` encoded by you) |
| **Complexity** | Simple `await` calls | More code, but full control |
| **Stateless servers** | Not compatible | Designed for stateless scenarios |
| **Fallback** | Automatic — SDK sends legacy requests | Manual — check `IsMrtrSupported` |
| **Multiple input types** | One at a time (elicit or sample) | Multiple in a single round |
