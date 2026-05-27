---
title: Multi Round-Trip Requests (MRTR)
author: halter73
description: How servers request client input during tool execution using Multi Round-Trip Requests.
uid: mrtr
---

# Multi Round-Trip Requests (MRTR)

<!-- mlc-disable-next-line -->
> [!WARNING]
> MRTR is part of the **`DRAFT-2026-v1`** revision of the MCP specification ([SEP-2322](https://github.com/modelcontextprotocol/modelcontextprotocol/pull/2322)). The wire format and API surface may change before the revision is ratified. See the [Experimental APIs](../../experimental.md) documentation for details on working with experimental APIs.

Multi Round-Trip Requests (MRTR) let a server tool request input from the client — such as [elicitation](xref:elicitation), [sampling](xref:sampling), or [roots](xref:roots) — as part of a single tool call, without requiring a separate server-to-client JSON-RPC request for each interaction. Instead of returning a final result, the server returns an **incomplete result** containing one or more input requests. The client fulfills those requests and retries the original tool call with the responses attached.

## Overview

MRTR is useful when:

- A tool needs user confirmation before proceeding (elicitation).
- A tool needs LLM reasoning from the client (sampling).
- A tool needs an updated list of client roots.
- A tool needs to perform multiple rounds of interaction in a single logical operation.
- A stateless server needs to orchestrate multi-step flows without keeping handler state in memory between rounds.

## How MRTR works

1. The client calls a tool on the server via `tools/call`.
2. The server tool determines it needs client input and returns an `InputRequiredResult` containing `inputRequests` and/or `requestState`.
3. The client resolves each input request (for example by prompting the user for elicitation, calling an LLM for sampling, or listing its roots).
4. The client retries the original `tools/call` with `inputResponses` (keyed to the input requests) and `requestState` echoed back.
5. The server processes the responses and either returns a final result or another `InputRequiredResult` for additional rounds.

## Opting in

MRTR activates when both peers negotiate protocol revision **`DRAFT-2026-v1`** during `initialize`. The C# SDK opts in by listing `DRAFT-2026-v1` as a supported protocol version on the client; servers automatically accept it when offered. No experimental flags are required.

```csharp
// Client
var clientOptions = new McpClientOptions
{
    ProtocolVersion = "DRAFT-2026-v1",
    Handlers = new McpClientHandlers
    {
        ElicitationHandler = HandleElicitationAsync,
        SamplingHandler = HandleSamplingAsync,
    }
};
```

Under `DRAFT-2026-v1`, MRTR is the **only** way to obtain client input from a server handler. The legacy server-to-client `elicitation/create`, `sampling/createMessage`, and `roots/list` request methods are removed; calling <xref:ModelContextProtocol.Server.McpServer.ElicitAsync*>, <xref:ModelContextProtocol.Server.McpServer.SampleAsync*>, or <xref:ModelContextProtocol.Server.McpServer.RequestRootsAsync*> on a server that negotiated `DRAFT-2026-v1` throws `InvalidOperationException`. Tools that need client input must throw <xref:ModelContextProtocol.Protocol.InputRequiredException> instead.

Under the current protocol revision (`2025-06-18` and earlier), `InputRequiredException` is still supported in stateful sessions via a backward-compatibility resolver — see [Compatibility](#compatibility) below.

## Authoring an MRTR tool

A tool participates in MRTR by throwing <xref:ModelContextProtocol.Protocol.InputRequiredException> with an <xref:ModelContextProtocol.Protocol.InputRequiredResult> describing what it needs. On retry, the client's responses arrive on the request parameters and the tool inspects them to decide what to do next.

### Checking MRTR support

Tools should check <xref:ModelContextProtocol.Server.McpServer.IsMrtrSupported> before throwing `InputRequiredException`. It returns `true` when either:

- The negotiated protocol revision is `DRAFT-2026-v1` (MRTR is native), or
- The session is stateful under the current protocol (the SDK can resolve input requests via legacy JSON-RPC and retry the handler).

```csharp
[McpServerTool, Description("A tool that uses MRTR")]
public static string MyTool(
    McpServer server,
    RequestContext<CallToolRequestParams> context)
{
    if (!server.IsMrtrSupported)
    {
        return "This tool requires a client that negotiates DRAFT-2026-v1, "
             + "or a stateful current-protocol session.";
    }

    // ... MRTR logic
}
```

### Returning an incomplete result

Throw <xref:ModelContextProtocol.Protocol.InputRequiredException> to return an incomplete result. The exception carries an <xref:ModelContextProtocol.Protocol.InputRequiredResult> containing `inputRequests` and/or `requestState`:

```csharp
[McpServerTool, Description("Tool managing its own MRTR flow")]
public static string AnswerTool(
    McpServer server,
    RequestContext<CallToolRequestParams> context,
    [Description("The user's question")] string question)
{
    var requestState = context.Params!.RequestState;
    var inputResponses = context.Params!.InputResponses;

    // On retry, process the client's responses
    if (requestState is not null && inputResponses is not null)
    {
        var elicitResult = inputResponses["user_answer"].Deserialize(InputResponse.ElicitResultTypeInfo);
        return $"You answered: {elicitResult?.Content?.FirstOrDefault().Value}";
    }

    if (!server.IsMrtrSupported)
    {
        return "MRTR is not supported by this client.";
    }

    // First call — request user input
    throw new InputRequiredException(
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

- <xref:ModelContextProtocol.Protocol.RequestParams.InputResponses> — a dictionary of client responses keyed by the same keys used in `inputRequests`.
- <xref:ModelContextProtocol.Protocol.RequestParams.RequestState> — the opaque state string echoed back by the client.

Use <xref:ModelContextProtocol.Protocol.InputResponse.Deserialize*> with the `JsonTypeInfo<T>` matching the response type. The expected type follows from the matching <xref:ModelContextProtocol.Protocol.InputRequest.Method> in the original `inputRequests` map — there is no on-the-wire discriminator.

- Elicitation — `response.Deserialize(InputResponse.ElicitResultTypeInfo)`
- Sampling — `response.Deserialize(InputResponse.SamplingResultTypeInfo)`
- Roots list — `response.Deserialize(InputResponse.RootsResultTypeInfo)`

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
    throw new InputRequiredException(
        requestState: Convert.ToBase64String(
            JsonSerializer.SerializeToUtf8Bytes(initialState)));
}
```

The client automatically retries `requestState`-only incomplete results, echoing the state back without needing to resolve any input requests.

### Multiple round trips

A tool can perform multiple rounds of interaction by throwing `InputRequiredException` multiple times across retries. Use `requestState` to track which round you're on:

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
        var name = inputResponses["name"].Deserialize(InputResponse.ElicitResultTypeInfo)?.Content?.FirstOrDefault().Value;
        var age = inputResponses["age"].Deserialize(InputResponse.ElicitResultTypeInfo)?.Content?.FirstOrDefault().Value;
        return $"Welcome, {name}! You are {age} years old.";
    }

    if (requestState == "step-1" && inputResponses is not null)
    {
        var name = inputResponses["name"].Deserialize(InputResponse.ElicitResultTypeInfo)?.Content?.FirstOrDefault().Value;

        // Second round — ask for age
        throw new InputRequiredException(
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
    throw new InputRequiredException(
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
    return "This tool requires interactive input. To use it:\n"
         + "1. Connect with a client that negotiates MCP protocol revision DRAFT-2026-v1, or\n"
         + "2. Use a stateful current-protocol session so the server can resolve the input requests for you.\n"
         + "\nStateless current-protocol sessions cannot resolve MRTR input requests.";
}
```

## Compatibility

The SDK supports `InputRequiredException` across two protocol revisions and two session modes:

| Negotiated protocol | Session mode | Behavior |
|---|---|---|
| `DRAFT-2026-v1` | Stateful | Native MRTR — `InputRequiredResult` is serialized directly to the wire. |
| `DRAFT-2026-v1` | Stateless | Native MRTR — `InputRequiredResult` is serialized directly to the wire. No server-side handler state needed. |
| Current (`2025-06-18` and earlier) | Stateful | Backward-compatibility resolver — the SDK sends standard `elicitation/create` / `sampling/createMessage` / `roots/list` JSON-RPC requests to the client, collects the responses, and retries the handler with `inputResponses` populated. Up to 10 retry rounds. |
| Current (`2025-06-18` and earlier) | Stateless | **Not supported** — `InputRequiredException` raises an `McpException`. The client doesn't speak MRTR, and the server can't resolve input requests via JSON-RPC without a persistent session. |

> [!NOTE]
> The backcompat resolver is intentionally limited to 10 retry rounds. Tools that need more rounds should require `DRAFT-2026-v1` (check `IsMrtrSupported`).

### Why `ElicitAsync` / `SampleAsync` / `RequestRootsAsync` throw under draft

The `DRAFT-2026-v1` revision removes the server-to-client `elicitation/create`, `sampling/createMessage`, and `roots/list` request methods entirely. Servers cannot use those request methods because clients no longer advertise the corresponding capabilities or implement handlers for them. The SDK fails fast with a clear `InvalidOperationException` so you can fix the call site before it manifests as a wire-level error.

Under the current protocol revision (`2025-06-18` and earlier), these methods continue to work normally and are the recommended way to do simple, one-shot client interactions. `InputRequiredException` is the way to write tools that work the same on both revisions.

### Future direction

The `DRAFT-2026-v1` revision is moving toward a stateless-only model: `Mcp-Session-Id` is being removed, and Streamable HTTP servers will run statelessly by default under the draft revision. When that happens, the `Stateful` row of the compatibility matrix above collapses into the `Stateless` row, and `InputRequiredException` becomes uniformly native across both. The current-protocol resolver path will remain for backward compatibility with older clients and stateful servers.

This work is a follow-up to the present PR.
