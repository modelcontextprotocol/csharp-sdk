---
title: Multi Round-Trip Requests (MRTR)
author: halter73
description: How servers request client input during tool execution using Multi Round-Trip Requests.
uid: mrtr
---

# Multi Round-Trip Requests (MRTR)

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
3. The client resolves each input request (for example, by prompting the user for elicitation, calling an LLM for sampling, or listing its roots).
4. The client retries the original `tools/call` with `inputResponses` (keyed to the input requests) and `requestState` echoed back.
5. The server processes the responses and either returns a final result or another `InputRequiredResult` for additional rounds.

## Opting in

MRTR activates when both peers negotiate protocol revision **`2026-07-28`**. The C# SDK client prefers `2026-07-28` by default — it probes with `server/discover` and falls back to an `initialize` handshake only when the server doesn't support it. Stateless HTTP servers accept `2026-07-28` automatically when a client offers it; HTTP servers configured with `Stateless = false` refuse that revision with `UnsupportedProtocolVersion` so dual-path clients can fall back to a session-capable revision. No experimental flags are required; pinning `ProtocolVersion` to an initialize-capable revision opts back out.

```csharp
// Client — the SDK prefers 2026-07-28 (and therefore MRTR) by default.
var clientOptions = new McpClientOptions
{
    Handlers = new McpClientHandlers
    {
        ElicitationHandler = HandleElicitationAsync,
        SamplingHandler = HandleSamplingAsync,
    }
};
```

Under `2026-07-28`, MRTR is the recommended way to obtain client input from a server handler. The spec removes the legacy server-to-client `elicitation/create`, `sampling/createMessage`, and `roots/list` request methods, so any code that needs to work on a `2026-07-28` Streamable HTTP server (where Streamable HTTP no longer supports sessions) must use `InputRequiredException` rather than <xref:ModelContextProtocol.Server.McpServer.ElicitAsync*>, <xref:ModelContextProtocol.Server.McpServer.SampleAsync*>, or <xref:ModelContextProtocol.Server.McpServer.RequestRootsAsync*>. The legacy methods still work on stateful sessions — including `2026-07-28` stdio sessions — but they throw `InvalidOperationException("X is not supported in stateless mode.")` on every stateless session.

Under `2025-11-25` and earlier, `InputRequiredException` is still supported in stateful sessions via a backward-compatibility resolver — see the [Compatibility](#compatibility) section.

## Authoring an MRTR tool

A tool participates in MRTR by throwing <xref:ModelContextProtocol.Protocol.InputRequiredException> with an <xref:ModelContextProtocol.Protocol.InputRequiredResult> describing what it needs. On retry, the client's responses arrive on the request parameters and the tool inspects them to decide what to do next.

### Checking MRTR support

Tools should check <xref:ModelContextProtocol.Server.McpServer.IsMrtrSupported> before throwing `InputRequiredException`. The property returns `true` when either:

- The negotiated protocol revision is `2026-07-28` (MRTR is native), or
- The session is stateful under protocol revision `2025-11-25` (the SDK can resolve input requests via legacy JSON-RPC and retry the handler).

```csharp
[McpServerTool, Description("A tool that uses MRTR")]
public static string MyTool(
    McpServer server,
    RequestContext<CallToolRequestParams> context)
{
    if (!server.IsMrtrSupported)
    {
        return "This tool requires a client that negotiates 2026-07-28, "
             + "or a stateful session using protocol revision 2025-11-25.";
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
        var elicitResult = inputResponses["user_answer"].Deserialize(InputResponse.ElicitResultJsonTypeInfo);
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

| Input       | Deserialize call                                                      |
|-------------|-----------------------------------------------------------------------|
| Elicitation | `response.Deserialize(InputResponse.ElicitResultJsonTypeInfo)`        |
| Sampling    | `response.Deserialize(InputResponse.CreateMessageResultJsonTypeInfo)` |
| Roots list  | `response.Deserialize(InputResponse.ListRootsResultJsonTypeInfo)`     |

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
        var name = inputResponses["name"].Deserialize(InputResponse.ElicitResultJsonTypeInfo)?.Content?.FirstOrDefault().Value;
        var age = inputResponses["age"].Deserialize(InputResponse.ElicitResultJsonTypeInfo)?.Content?.FirstOrDefault().Value;
        return $"Welcome, {name}! You are {age} years old.";
    }

    if (requestState == "step-1" && inputResponses is not null)
    {
        var name = inputResponses["name"].Deserialize(InputResponse.ElicitResultJsonTypeInfo)?.Content?.FirstOrDefault().Value;

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
         + "1. Connect with a client that negotiates MCP protocol revision 2026-07-28, or\n"
         + "2. Use a stateful session using protocol revision 2025-11-25 so the server can resolve the input requests for you.\n"
         + "\nStateless sessions using protocol revision 2025-11-25 and earlier cannot resolve MRTR input requests.";
}
```

## Compatibility

The SDK supports `InputRequiredException` across two protocol revisions and two session modes:

| Negotiated protocol              | Session mode | Behavior                                                                                                                                            |
|----------------------------------|--------------|-----------------------------------------------------------------------------------------------------------------------------------------------------|
| `2026-07-28`                     | Stateful     | Native MRTR — `InputRequiredResult` is serialized directly to the wire.                                                                             |
| `2026-07-28`                     | Stateless    | Native MRTR — `InputRequiredResult` is serialized directly to the wire. No server-side handler state needed.                                       |
| `2025-11-25` and earlier         | Stateful     | Backward-compatibility resolver — the SDK sends standard `elicitation/create` / `sampling/createMessage` / `roots/list` JSON-RPC requests to the client, collects the responses, and retries the handler with `inputResponses` populated. Up to 10 retry rounds. |
| `2025-11-25` and earlier         | Stateless    | **Not supported** — `InputRequiredException` raises an `McpException`. The client doesn't speak MRTR, and the server can't resolve input requests via JSON-RPC without a persistent session. |

> [!NOTE]
> The backcompat resolver is intentionally limited to 10 retry rounds. Tools that need more rounds should require `2026-07-28` (check `IsMrtrSupported`).

### Why `ElicitAsync` / `SampleAsync` / `RequestRootsAsync` throw on stateless servers

`ElicitAsync` / `SampleAsync` / `RequestRootsAsync` issue a JSON-RPC request to the client and wait for the response on the same session. Stateless servers don't have a persistent session to wait on, so the SDK fails fast with `InvalidOperationException("X is not supported in stateless mode.")` (the check is `McpServer.ClientCapabilities is null`, which is the SDK's proxy for stateless).

Under `2025-11-25` and earlier, stdio and stateful Streamable HTTP keep `ClientCapabilities` populated, so the legacy methods work normally and remain the recommended way to do one-shot client interactions. Under `2026-07-28`, the spec removes those request methods from Streamable HTTP entirely; the SDK still allows the legacy methods on `2026-07-28` stdio sessions because stdio is implicitly single-process / stateful and the client handler is wired up regardless of negotiated revision. `InputRequiredException` is the way to write tools that work on every supported configuration.

Because `2026-07-28` removes `Mcp-Session-Id` (SEP-2567) and the `initialize` handshake (SEP-2575), Streamable HTTP can serve that revision only through the stateless path. The `Stateful` row for `2026-07-28` in the compatibility matrix above therefore applies to stdio and other non-HTTP stateful sessions; an HTTP server explicitly set to `Stateless = false` refuses `2026-07-28` with `UnsupportedProtocolVersion` and creates a session only when an older client falls back to `initialize`.
