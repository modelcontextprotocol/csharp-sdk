---
title: Sessions
author: halter73
description: How sessions work in the MCP C# SDK and when to use stateless vs. stateful mode for HTTP servers.
uid: sessions
---

# Sessions

The MCP [Streamable HTTP transport] uses an `Mcp-Session-Id` HTTP header to associate multiple requests with a single logical session. Sessions enable features like server-to-client requests (sampling, elicitation, roots), unsolicited notifications, resource subscriptions, and session-scoped state. However, **most servers don't need sessions and should run in stateless mode** to avoid unnecessary complexity, memory overhead, and deployment constraints.

[Streamable HTTP transport]: https://modelcontextprotocol.io/specification/2025-11-25/basic/transports#streamable-http

**Quick guide — which mode should I use?**

- Does your server need to send requests _to_ the client (sampling, elicitation, roots)?  → **Use stateful.**
- Does your server send unsolicited notifications or support resource subscriptions?  → **Use stateful.**
- Do you need to support clients that only speak the [legacy SSE transport](#sse-legacy)?  → **Use stateful** with <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.EnableLegacySse> (disabled by default due to [backpressure concerns](#sse-legacy-1)).
- Does your server manage per-client state that concurrent agents must not share (isolated environments, parallel workspaces)?  → **Use stateful.**
- Are you debugging a typically-stdio server over HTTP and want editors to be able to reset state by reconnecting?  → **Use stateful.**
- Otherwise → **Use stateless** (`options.Stateless = true`).

<!-- mlc-disable-next-line -->
> [!NOTE]
> **Why isn't stateless the default?** Stateful mode remains the default for backward compatibility and because it is the only HTTP mode with full feature parity with [stdio](xref:transports) (server-to-client requests, unsolicited notifications, subscriptions). Stateless is the recommended choice when you don't need those features. If your server _does_ depend on stateful behavior, consider setting `Stateless = false` explicitly so your code is resilient to a potential future default change once [MRTR](https://github.com/modelcontextprotocol/csharp-sdk/pull/1458) or similar mechanisms bring server-to-client interactions to stateless mode.

## Stateless mode (recommended)

Stateless mode is the recommended default for HTTP-based MCP servers. When enabled, the server doesn't track any state between requests, doesn't use the `Mcp-Session-Id` header, and treats each request independently. This is the simplest and most scalable deployment model.

### Enabling stateless mode

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
    })
    .WithTools<MyTools>();

var app = builder.Build();
app.MapMcp();
app.Run();
```

### What stateless mode disables

When <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.Stateless> is `true`:

- <xref:ModelContextProtocol.McpSession.SessionId> is `null`, and the `Mcp-Session-Id` header is not sent or expected
- Each HTTP request creates a fresh server context — no state carries over between requests
- <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.ConfigureSessionOptions> still works, but is called **per HTTP request** rather than once per session (see [Per-request configuration in stateless mode](#per-request-configuration-in-stateless-mode))
- The `GET` and `DELETE` MCP endpoints are not mapped, and [legacy SSE endpoints](#sse-legacy) (`/sse` and `/message`) are always disabled in stateless mode — clients that only support the legacy SSE transport cannot connect
- **Server-to-client requests are disabled**, including:
  - [Sampling](xref:sampling) (`SampleAsync`)
  - [Elicitation](xref:elicitation) (`ElicitAsync`)
  - [Roots](xref:roots) (`RequestRootsAsync`)
- Unsolicited server-to-client notifications (e.g., resource update notifications, logging messages) are not supported
- [Tasks](xref:tasks) **are supported** — the task store is shared across ephemeral server instances. However, task-augmented sampling and elicitation are disabled because they require server-to-client requests.

These restrictions exist because in a stateless deployment, responses from the client could arrive at any server instance — not necessarily the one that sent the request.

### When to use stateless mode

Use stateless mode when your server:

- Exposes tools that are pure functions (take input, return output)
- Doesn't need to ask the client for user input (elicitation) or LLM completions (sampling)
- Doesn't need to send unsolicited notifications to the client
- Needs to scale horizontally behind a load balancer without session affinity
- Is deployed to serverless environments (Azure Functions, AWS Lambda, etc.)

Most MCP servers fall into this category. Tools that call APIs, query databases, process data, or return computed results are all natural fits for stateless mode.

<!-- mlc-disable-next-line -->
> [!TIP]
> If you're unsure whether you need sessions, start with stateless mode. You can always switch to stateful mode later if you need server-to-client requests or other session features.

### What you give up with stateless mode

Stateless mode trades features for simplicity:

- **No server-to-client requests.** Sampling, elicitation, and roots all require the server to send a JSON-RPC request back to the client over a persistent connection. Stateless mode has no such connection. The proposed [MRTR mechanism](https://github.com/modelcontextprotocol/csharp-sdk/pull/1458) is designed to solve this, but it is not yet available.
- **No push notifications.** The server cannot send unsolicited messages — log entries, resource-change events, or progress updates outside the scope of a tool call response. Every notification must be part of a direct response to a client request.
- **No concurrent client isolation.** Every request is independent. The server cannot distinguish between two agents calling the same tool simultaneously, and there is no mechanism to maintain separate state per client.
- **No state reset on reconnect.** When a client disconnects and reconnects (e.g., an editor restarting), stateless servers have no concept of "the previous connection." There is no session to close and no fresh session to start — because there was never a session to begin with. If your server holds any external state, you must manage cleanup through other means.

### Stateless alternatives for server-to-client interactions

<!-- mlc-disable-next-line -->
> [!NOTE]
> Multi Round-Trip Requests (MRTR) is a proposed experimental feature that is not yet available. See PR [#1458](https://github.com/modelcontextprotocol/csharp-sdk/pull/1458) for the reference implementation and specification proposal.

The traditional approach to server-to-client interactions (elicitation, sampling, roots) requires sessions because the server must hold an open connection to send JSON-RPC requests back to the client. [Multi Round-Trip Requests (MRTR)](https://github.com/modelcontextprotocol/csharp-sdk/pull/1458) is a proposed alternative that works with stateless servers by inverting the communication model — instead of sending a request, the server returns an **incomplete result** that tells the client what input is needed. The client fulfills the requests and retries the tool call with the responses attached.

This means servers that need user confirmation, LLM reasoning, or other client input can still run in stateless mode when both sides support MRTR.

## Stateful mode (sessions)

When <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.Stateless> is `false` (the default), the server assigns an `Mcp-Session-Id` to each client during the `initialize` handshake. The client must include this header in all subsequent requests. The server maintains an in-memory session for each connected client, enabling:

- Server-to-client requests (sampling, elicitation, roots) via an open SSE stream
- Unsolicited notifications (resource updates, logging messages)
- Resource subscriptions
- Session-scoped state (e.g., `RunSessionHandler`, state that persists across multiple requests within a session)

### When to use stateful mode

Use stateful mode when your server needs one or more of:

- **Server-to-client requests**: Tools that call `ElicitAsync`, `SampleAsync`, or `RequestRootsAsync` to interact with the client
- **Unsolicited notifications**: Sending resource-changed notifications or log messages without a preceding client request
- **Resource subscriptions**: Clients subscribing to resource changes and receiving updates
- **Legacy SSE client support**: Clients that only speak the [legacy SSE transport](#sse-legacy) — requires <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.EnableLegacySse> (disabled by default)
- **Session-scoped state**: Logic that must persist across multiple requests within the same session
- **Concurrent client isolation**: Multiple agents or editor instances connecting simultaneously, where per-client state must not leak between users — separate working environments, independent scratch state, or parallel simulations where each participant needs its own context. The server — not the model — controls when sessions are created, so the harness decides the boundaries of isolation.
- **Local development and debugging**: Testing a typically-stdio server over HTTP where you want to attach a debugger, see log output on stdout, and have editors like Claude Code, GitHub Copilot in VS Code, and Cursor reset the server's state by starting a new session — without requiring a process restart. This closely mirrors the stdio experience where restarting the server process gives the client a clean slate.

The [deployment considerations](#deployment-considerations) below are real concerns for production, internet-facing services — but many MCP servers don't run in that context. For single-instance servers, internal tools, and dev/test clusters, session affinity and memory overhead are less of a concern, and sessions provide the richest feature set.

## Comparison

| Consideration | Stateless | Stateful |
|---|---|---|
| **Deployment** | Any topology — load balancer, serverless, multi-instance | Requires session affinity (sticky sessions) |
| **Scaling** | Horizontal scaling without constraints | Limited by session-affinity routing |
| **Server restarts** | No impact — each request is independent | All sessions lost; clients must reinitialize |
| **Memory** | Per-request only | Per-session (default: up to 10,000 sessions × 2 hours) |
| **Server-to-client requests** | Not supported (see [MRTR proposal](https://github.com/modelcontextprotocol/csharp-sdk/pull/1458) for a stateless alternative) | Supported (sampling, elicitation, roots) |
| **Unsolicited notifications** | Not supported | Supported (resource updates, logging) |
| **Resource subscriptions** | Not supported | Supported |
| **Client compatibility** | Works with all Streamable HTTP clients | Also supports legacy SSE-only clients via <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.EnableLegacySse> (disabled by default), but some Streamable HTTP clients [may not send `Mcp-Session-Id` correctly](#deployment-considerations) |
| **Local development** | Works, but no way to reset server state from the editor | Editors can reset state by starting a new session without restarting the process |
| **Concurrent client isolation** | No distinction between clients — all requests are independent | Each client gets its own session with isolated state |
| **State reset on reconnect** | No concept of reconnection — every request stands alone | Client reconnection starts a new session with a clean slate |
| **[Tasks](xref:tasks)** | Supported — shared task store, no per-session isolation | Supported — task store scoped per session |

## Transports and sessions

### Streamable HTTP

#### Session lifecycle

A session begins when a client sends an `initialize` JSON-RPC request without an `Mcp-Session-Id` header. The server:

1. Creates a new session with a unique session ID
2. Calls <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.ConfigureSessionOptions> (if configured) to customize the session's `McpServerOptions`
3. Starts the MCP server for the session
4. Returns the session ID in the `Mcp-Session-Id` response header along with the `InitializeResult`

All subsequent requests from the client must include this session ID.

#### Activity tracking

The server tracks the last activity time for each Streamable HTTP session. Activity is recorded when:

- A request arrives for the session (POST or GET)
- A response is sent for the session

#### Idle timeout

Streamable HTTP sessions that have no activity for the duration of <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.IdleTimeout> (default: **2 hours**) are automatically closed. The idle timeout is checked in the background every 5 seconds.

A client can keep its session alive by maintaining any open HTTP request (e.g., a long-running POST with a streamed response or an open `GET` for unsolicited messages). Sessions with active requests are never considered idle.

When a session times out:

- The session's `McpServer` is disposed
- Any pending requests receive cancellation
- A client trying to use the expired session ID receives a `404 Session not found` error and should start a new session

You can disable idle timeout by setting it to `Timeout.InfiniteTimeSpan`, though this is not recommended for production deployments.

#### Maximum idle session count

<xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.MaxIdleSessionCount> (default: **10,000**) limits how many idle Streamable HTTP sessions can exist simultaneously. If this limit is exceeded:

- A critical error is logged
- The oldest idle sessions are terminated (even if they haven't reached their idle timeout)
- Termination continues until the idle count is back below the limit

Sessions with any active HTTP request don't count toward this limit.

#### Termination

Streamable HTTP sessions can be terminated by:

- **Client DELETE request**: The client sends an HTTP `DELETE` to the session endpoint with its `Mcp-Session-Id`
- **Idle timeout**: The session exceeds the idle timeout without activity
- **Max idle count**: The server exceeds its maximum idle session count and prunes the oldest sessions
- **Server shutdown**: All sessions are disposed when the server shuts down

#### Deployment considerations

Stateful sessions introduce several challenges for production, internet-facing services:

**Session affinity required.** All requests for a given session must reach the same server instance, because sessions live in memory. If you deploy behind a load balancer, you must configure session affinity (sticky sessions) to route requests to the correct instance. Without session affinity, clients will receive `404 Session not found` errors.

**Memory consumption.** Each session consumes memory on the server for the lifetime of the session. The default idle timeout is **2 hours**, and the default maximum idle session count is **10,000**. A server with many concurrent clients can accumulate significant memory usage. Monitor your idle session count and tune <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.IdleTimeout> and <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.MaxIdleSessionCount> to match your workload.

**Server restarts lose all sessions.** Sessions are stored in memory by default. When the server restarts (for deployments, crashes, or scaling events), all sessions are lost. Clients must reinitialize their sessions, which some clients may not handle gracefully. You can mitigate this with <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.SessionMigrationHandler>, but this adds complexity. See [Session migration](#session-migration) for details.

**Clients that don't send Mcp-Session-Id.** Some MCP clients may not send the `Mcp-Session-Id` header on every request. When this happens, the server responds with an error: `"Bad Request: A new session can only be created by an initialize request."` This can happen after a server restart, when a client loses its session ID, or when a client simply doesn't support sessions. If you see this error, consider whether your server actually needs sessions — and if not, switch to stateless mode.

**No built-in backpressure on advanced features.** By default, each JSON-RPC request holds its HTTP POST open until the handler responds — providing natural HTTP/2 backpressure. However, advanced features like <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.EventStreamStore> and [Tasks](xref:tasks) can decouple handler execution from the HTTP request, removing this protection. See [Request backpressure](#request-backpressure) for details and mitigations.

### SSE (legacy)

The legacy [SSE (Server-Sent Events)](https://modelcontextprotocol.io/specification/2024-11-05/basic/transports#http-with-sse) transport is also supported by `MapMcp()` and always uses stateful mode. Legacy SSE endpoints (`/sse` and `/message`) are **disabled by default** because the SSE transport has [no built-in HTTP-level backpressure](#sse-legacy-1). To enable them, set <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.EnableLegacySse> to `true` — this property is marked `[Obsolete]` with a diagnostic warning (`MCP9003`) to signal that it should only be used when you need to support legacy SSE-only clients and understand the backpressure implications. Alternatively, set the `ModelContextProtocol.AspNetCore.EnableLegacySse` [AppContext switch](https://learn.microsoft.com/dotnet/api/system.appcontext) to `true`.

> [!NOTE]
> SSE endpoints are always disabled in stateless mode regardless of the `EnableLegacySse` setting, because the GET and POST requests must be handled by the same server process sharing in-memory session state.

#### How SSE sessions work

1. The client connects to the `/sse` endpoint with a GET request
2. The server generates a session ID and sends a `/message?sessionId={id}` URL as the first SSE event
3. The client sends JSON-RPC messages as POST requests to that `/message?sessionId={id}` URL
4. The server streams responses and unsolicited messages back over the open SSE GET stream

Unlike Streamable HTTP which uses the `Mcp-Session-Id` header, legacy SSE passes the session ID as a query string parameter on the `/message` endpoint.

#### Session lifetime

SSE session lifetime is tied directly to the GET SSE stream. When the client disconnects (detected via `HttpContext.RequestAborted`), or the server shuts down (via `IHostApplicationLifetime.ApplicationStopping`), the session is immediately removed. There is no idle timeout or maximum idle session count for SSE sessions — the session exists exactly as long as the SSE connection is open.

This makes SSE sessions behave similarly to [stdio](#stdio-transport): the session is implicit in the connection lifetime, and disconnection is the only termination mechanism.

#### Configuration

<xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.ConfigureSessionOptions> and <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.RunSessionHandler> both work with SSE sessions. They are called during the `/sse` GET request handler, and services resolve from the GET request's `HttpContext.RequestServices`. [User binding](#user-binding) also works — the authenticated user is captured from the GET request and verified on each POST to `/message`.

### stdio transport

The [stdio transport](xref:transports) is inherently single-session. The client launches the server as a child process and communicates over stdin/stdout. There is exactly one session per process, the session starts when the process starts, and it ends when the process exits.

Because there is only one connection, stdio servers don't need session IDs or any explicit session management. The session is implicit in the process boundary. This makes stdio the simplest transport to use, and it naturally supports all server-to-client features (sampling, elicitation, roots) because there is always exactly one client connected.

However, stdio servers cannot be shared between multiple clients. Each client needs its own server process. This is fine for local tool integrations (IDEs, CLI tools) but not suitable for remote or multi-tenant scenarios — use [Streamable HTTP](xref:transports) for those. For details on how DI scopes work with stdio, see [Service lifetimes and DI scopes](#service-lifetimes-and-di-scopes).

## Server configuration

### Configuration reference

All session-related configuration is on <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions>, configured via `WithHttpTransport`:

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        // Recommended for servers that don't need sessions.
        options.Stateless = true;

        // --- Options below only apply to stateful (non-stateless) mode ---

        // How long a session can be idle before being closed (default: 2 hours)
        options.IdleTimeout = TimeSpan.FromMinutes(30);

        // Maximum number of idle sessions in memory (default: 10,000)
        options.MaxIdleSessionCount = 1_000;

        // Customize McpServerOptions per session with access to HttpContext
        options.ConfigureSessionOptions = async (httpContext, mcpServerOptions, cancellationToken) =>
        {
            // Example: customize tools based on the authenticated user's roles
            var user = httpContext.User;
            if (user.IsInRole("admin"))
            {
                mcpServerOptions.ToolCollection = [.. adminTools];
            }
        };
    });
```

### Property reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.Stateless> | `bool` | `false` | Enables stateless mode. No sessions, no `Mcp-Session-Id` header, no server-to-client requests. |
| <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.IdleTimeout> | `TimeSpan` | 2 hours | Duration of inactivity before a session is closed. Checked every 5 seconds. |
| <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.MaxIdleSessionCount> | `int` | 10,000 | Maximum idle sessions before the oldest are forcibly terminated. |
| <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.ConfigureSessionOptions> | `Func<HttpContext, McpServerOptions, CancellationToken, Task>?` | `null` | Per-session callback to customize `McpServerOptions` with access to `HttpContext`. In stateless mode, this runs on every HTTP request. |
| <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.RunSessionHandler> | `Func<HttpContext, McpServer, CancellationToken, Task>?` | `null` | *(Experimental)* Custom session lifecycle handler. Consider `ConfigureSessionOptions` instead. |
| <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.SessionMigrationHandler> | `ISessionMigrationHandler?` | `null` | Enables cross-instance session migration. Can also be registered in DI. |
| <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.EventStreamStore> | `ISseEventStreamStore?` | `null` | Stores SSE events for session resumability via `Last-Event-ID`. Can also be registered in DI. |
| <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.PerSessionExecutionContext> | `bool` | `false` | Uses a single `ExecutionContext` for the entire session instead of per-request. Enables session-scoped `AsyncLocal<T>` values but prevents `IHttpContextAccessor` from working in handlers. |

### ConfigureSessionOptions

<xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.ConfigureSessionOptions> is called when the server creates a new MCP server context, before the server starts processing requests. It receives the `HttpContext` from the `initialize` request, allowing you to customize the server based on the request (authentication, headers, route parameters, etc.).

In **stateful mode**, this callback runs once per session — when the client's initial `initialize` request creates the session.

```csharp
options.ConfigureSessionOptions = async (httpContext, mcpServerOptions, cancellationToken) =>
{
    // Filter available tools based on a route parameter
    var category = httpContext.Request.RouteValues["category"]?.ToString() ?? "all";
    mcpServerOptions.ToolCollection = GetToolsForCategory(category);

    // Set server info based on the authenticated user
    var userName = httpContext.User.Identity?.Name;
    mcpServerOptions.ServerInfo = new() { Name = $"MCP Server ({userName})" };
};
```

See the [AspNetCoreMcpPerSessionTools](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/AspNetCoreMcpPerSessionTools) sample for a complete example that filters tools based on route parameters.

#### Per-request configuration in stateless mode

In **stateless mode**, `ConfigureSessionOptions` is called on **every HTTP request** because each request creates a fresh server context. This makes it useful for per-request customization based on headers, authentication, or other request-specific data — similar to middleware:

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
        options.ConfigureSessionOptions = (httpContext, mcpServerOptions, cancellationToken) =>
        {
            // This runs on every request in stateless mode, so you can use the
            // current HttpContext to customize tools, prompts, or resources.
            var apiVersion = httpContext.Request.Headers["X-Api-Version"].ToString();
            mcpServerOptions.ToolCollection = GetToolsForVersion(apiVersion);
            return Task.CompletedTask;
        };
    })
    .WithTools<DefaultTools>();
```
 
## Security

### User binding

When authentication is configured, the server automatically binds sessions to the authenticated user. This prevents one user from hijacking another user's session.

#### How it works

1. When a session is created, the server captures the authenticated user's identity from `HttpContext.User`
2. The server extracts a user ID claim in priority order:
   - `ClaimTypes.NameIdentifier` (`http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`)
   - `"sub"` (OpenID Connect subject claim)
   - `ClaimTypes.Upn` (`http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn`)
3. On each subsequent request, the server validates that the current user matches the session's original user
4. If there's a mismatch, the server responds with `403 Forbidden`

This binding is automatic — no configuration is needed. If no authentication middleware is configured, user binding is skipped (the session is not bound to any user).

## Service lifetimes and DI scopes

How the server resolves scoped services depends on the transport and session mode. The <xref:ModelContextProtocol.Server.McpServerOptions.ScopeRequests> property controls whether the server creates a new `IServiceProvider` scope for each handler invocation.

### Stateful HTTP

In stateful mode, the server's <xref:ModelContextProtocol.Server.McpServer.Services> is the application-level `IServiceProvider` — not a per-request scope. Because the server outlives individual HTTP requests, <xref:ModelContextProtocol.Server.McpServerOptions.ScopeRequests> defaults to `true`: each handler invocation (tool call, resource read, etc.) creates a new scope.

This means:

- **Scoped services** are created fresh for each handler invocation and disposed when the handler completes
- **Singleton services** resolve from the application container as usual
- **Transient services** create a new instance per resolution, as usual

### Stateless HTTP

In stateless mode, the server uses ASP.NET Core's per-request `HttpContext.RequestServices` as its service provider, and <xref:ModelContextProtocol.Server.McpServerOptions.ScopeRequests> is automatically set to `false`. No additional scopes are created — handlers share the same HTTP request scope that middleware and other ASP.NET Core components use.

This means:

- **Scoped services** behave exactly like any other ASP.NET Core request-scoped service — middleware can set state on a scoped service and the tool handler will see it
- The DI lifetime model is identical to a standard ASP.NET Core controller or minimal API endpoint

### stdio

The stdio transport creates a single server for the lifetime of the process. The server's <xref:ModelContextProtocol.Server.McpServer.Services> is the application-level `IServiceProvider`. By default, <xref:ModelContextProtocol.Server.McpServerOptions.ScopeRequests> is `true`, so each handler invocation gets its own scope — the same behavior as stateful HTTP.

### McpServer.Create (custom transports)

When you create a server directly with <xref:ModelContextProtocol.Server.McpServer.Create*>, you control the `IServiceProvider` and transport yourself. If you pass an already-scoped provider, you can set <xref:ModelContextProtocol.Server.McpServerOptions.ScopeRequests> to `false` to avoid creating redundant nested scopes. The [InMemoryTransport sample](https://github.com/modelcontextprotocol/csharp-sdk/blob/51a4fde4d9cfa12ef9430deef7daeaac36625be8/samples/InMemoryTransport/Program.cs#L6-L14) shows a minimal example of using `McpServer.Create` with in-memory pipes:

```csharp
Pipe clientToServerPipe = new(), serverToClientPipe = new();

await using var scope = serviceProvider.CreateAsyncScope();

await using McpServer server = McpServer.Create(
    new StreamServerTransport(clientToServerPipe.Reader.AsStream(), serverToClientPipe.Writer.AsStream()),
    new McpServerOptions
    {
        ScopeRequests = false, // The scope is already managed externally.
        ToolCollection = [McpServerTool.Create((string arg) => $"Echo: {arg}", new() { Name = "Echo" })]
    },
    serviceProvider: scope.ServiceProvider);
```

### DI scope summary

| Mode | Service provider | ScopeRequests | Handler scope |
|------|-----------------|---------------|---------------|
| **Stateful HTTP** | Application services | `true` (default) | New scope per handler invocation |
| **Stateless HTTP** | `HttpContext.RequestServices` | `false` (forced) | Shared HTTP request scope |
| **stdio** | Application services | `true` (default, configurable) | New scope per handler invocation |
| **McpServer.Create** | Caller-provided | Caller-controlled | Depends on `ScopeRequests` and whether the provider is already scoped |

## Cancellation and disposal

Every tool, prompt, and resource handler can receive a `CancellationToken`. The source and behavior of that token depends on the transport and session mode. The SDK also supports the MCP [cancellation protocol](https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/cancellation) for client-initiated cancellation of individual requests.

### Handler cancellation tokens

| Mode | Token source | Cancelled when |
|------|-------------|----------------|
| **Stateless HTTP** | `HttpContext.RequestAborted` | Client disconnects, or ASP.NET Core shuts down. Identical to a standard minimal API or controller action. |
| **Stateful Streamable HTTP** | Linked token: HTTP request + application shutdown + session disposal | Client disconnects, `ApplicationStopping` fires, or the session is terminated (idle timeout, DELETE, max idle count). |
| **SSE (legacy)** | Linked token: GET request + application shutdown | Client disconnects the SSE stream, or `ApplicationStopping` fires. The entire session terminates with the GET stream. |
| **stdio** | Token passed to `McpServer.RunAsync()` | stdin EOF (client process exits), or the token is cancelled (e.g., host shutdown via Ctrl+C). |

Stateless mode has the simplest cancellation story: the handler's `CancellationToken` is `HttpContext.RequestAborted` — the same token any ASP.NET Core endpoint receives. No additional tokens, linked sources, or session-level lifecycle to reason about.

### Client-initiated cancellation

In stateful modes (Streamable HTTP, SSE, stdio), a client can cancel a specific in-flight request by sending a [`notifications/cancelled`](https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/cancellation) notification with the request ID. The SDK looks up the running handler and cancels its `CancellationToken`. The handler receives an `OperationCanceledException` like any other cancellation.

- Invalid or unknown request IDs are silently ignored
- In stateless mode, there is no persistent session to receive the notification on, so client-initiated cancellation does not apply
- For [task-augmented requests](xref:tasks), the MCP specification requires using [`tasks/cancel`](https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/tasks#cancelling-tasks) instead of `notifications/cancelled`. The SDK uses a separate cancellation token per task (independent of the original HTTP request), so `tasks/cancel` can cancel a task even after the initial request has completed. See [Tasks and session modes](#tasks-and-session-modes) for details.

### Server and session disposal

When an `McpServer` is disposed — whether due to session termination, transport closure, or application shutdown — the SDK **awaits all in-flight handlers** before `DisposeAsync()` returns. This means:

- Handlers have an opportunity to complete cleanup (e.g., flushing writes, releasing locks)
- Scoped services created for the handler are disposed after the handler completes
- The SDK logs each handler's completion at `Information` level, including elapsed time

#### Graceful shutdown in ASP.NET Core

When `ApplicationStopping` fires (e.g., `SIGTERM`, `Ctrl+C`, `app.StopAsync()`), the SDK immediately cancels active SSE and GET streams so that connected clients don't block shutdown. In-flight POST request handlers continue running and are awaited before the server finishes disposing. The total shutdown time is bounded by ASP.NET Core's `HostOptions.ShutdownTimeout` (default: **30 seconds**). In practice, the SDK completes shutdown well within this limit.

For stateless servers, shutdown is even simpler: each request is independent, so there are no long-lived sessions to drain — just standard ASP.NET Core request completion.

#### stdio process lifecycle

- **Graceful shutdown** (stdin EOF, `SIGTERM`, `Ctrl+C`): The transport closes, in-flight handlers are awaited, and `McpServer.DisposeAsync()` runs normally.
- **Process kill** (`SIGKILL`): No cleanup occurs. Handlers are interrupted mid-execution, and no disposal code runs. This is inherent to process-level termination and not specific to the SDK.

### Stateless per-request logging

In stateless mode, each HTTP request creates and disposes a short-lived `McpServer` instance. This produces session lifecycle log entries at `Trace` level (`session created` / `session disposed`) for every request. These are typically invisible at default log levels but may appear when troubleshooting with verbose logging enabled. There is no user-facing `initialize` handshake in stateless mode — the SDK handles the per-request server lifecycle internally.

### Tasks and session modes

[Tasks](xref:tasks) enable a "call-now, fetch-later" pattern for long-running tool calls. Task support depends on having an <xref:ModelContextProtocol.IMcpTaskStore> configured (`McpServerOptions.TaskStore`), and behavior differs between session modes.

#### Stateless mode

Tasks are a natural fit for stateless servers. The client sends a task-augmented `tools/call` request, receives a task ID immediately, and polls for completion with `tasks/get` or `tasks/result` on subsequent independent HTTP requests. Because each request creates an ephemeral `McpServer` that shares the same `IMcpTaskStore`, all task operations work without any persistent session.

In stateless mode, there is no `SessionId`, so the task store does not apply session-based isolation. All tasks are accessible from any request to the same server. This is typically fine for single-purpose servers or when authentication middleware already identifies the caller.

#### Stateful mode

In stateful mode, the `IMcpTaskStore` receives the session's `SessionId` on every operation — `CreateTaskAsync`, `GetTaskAsync`, `ListTasksAsync`, `CancelTaskAsync`, etc. The built-in <xref:ModelContextProtocol.InMemoryMcpTaskStore> enforces session isolation: tasks created in one session cannot be accessed from another.

Tasks can outlive individual HTTP requests because the tool executes in the background after returning the initial `CreateTaskResult`. Task cleanup is governed by the task's TTL (time-to-live), not by session termination. However, the `InMemoryMcpTaskStore` loses all tasks if the server process restarts. For durable tasks, implement a custom <xref:ModelContextProtocol.IMcpTaskStore> backed by an external store. See [Fault-tolerant task implementations](xref:tasks#fault-tolerant-task-implementations) for guidance.

#### Task cancellation vs request cancellation

The MCP specification defines two distinct cancellation mechanisms:

- **`notifications/cancelled`** cancels a regular in-flight request by its JSON-RPC request ID. The SDK looks up the handler's `CancellationToken` and cancels it. This is a fire-and-forget notification with no response.
- **`tasks/cancel`** cancels a task by its task ID. The SDK signals a separate per-task `CancellationToken` (independent of the original request) and updates the task's status to `cancelled` in the store. This is a request-response operation that returns the final task state.

For task-augmented requests, the specification [requires](https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/cancellation) using `tasks/cancel` instead of `notifications/cancelled`.

### Request backpressure

How well the server is protected against a flood of concurrent requests depends on the session mode and which advanced features are enabled. **In the default configuration, stateful and stateless modes provide identical HTTP-level backpressure** — both hold the POST response open while the handler runs, so HTTP/2's `MaxStreamsPerConnection` (default: **100**) naturally limits concurrent handlers per connection. The unbounded cases (legacy SSE, `EventStreamStore`, Tasks) are all **opt-in** advanced features.

#### Default stateful mode (no EventStreamStore, no tasks)

In the default configuration, each JSON-RPC request holds its POST response open until the handler produces a result. The POST response body is an SSE stream that carries the JSON-RPC response, and the server awaits the handler's completion before closing it. This means:

- Each in-flight handler occupies one HTTP/2 stream
- Kestrel's `MaxStreamsPerConnection` (default: **100**) limits concurrent handlers per connection
- This is the same backpressure model as **gRPC unary calls** — one request occupies one stream until the response is sent

One difference from gRPC: handler cancellation tokens are linked to the **session** lifetime, not `HttpContext.RequestAborted`. If a client disconnects from a POST mid-flight, the handler continues running until it completes or the session is terminated. But the client has freed a stream slot, so it can submit a new request — meaning the server could accumulate up to `MaxStreamsPerConnection` handlers that outlive their original connections. In practice this is bounded and comparable to how gRPC handlers behave when the client cancels an RPC.

For comparison, ASP.NET Core SignalR limits concurrent hub invocations per client to **1** by default (`MaximumParallelInvocationsPerClient`). Default stateful MCP is less restrictive but still bounded by HTTP/2 stream limits.

#### SSE (legacy — opt-in only)

Legacy SSE endpoints are [disabled by default](#sse-legacy) and must be explicitly enabled via <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.EnableLegacySse>. This is the primary reason they are disabled — the SSE transport has no built-in HTTP-level backpressure.

The legacy SSE transport separates the request and response channels: clients POST JSON-RPC messages to `/message` and receive responses through a long-lived GET SSE stream on `/sse`. The POST endpoint returns **202 Accepted immediately** after queuing the message — it does not wait for the handler to complete. This means there is **no HTTP-level backpressure** on handler concurrency, because each POST frees its connection immediately regardless of how long the handler runs.

Internally, handlers are dispatched with the same fire-and-forget pattern as Streamable HTTP (`_ = ProcessMessageAsync()`). A client can send unlimited POST requests to `/message` while keeping the GET stream open, and each one spawns a concurrent handler with no built-in limit.

The GET stream does provide **session lifetime bounds**: handler cancellation tokens are linked to the GET request's `HttpContext.RequestAborted`, so when the client disconnects the SSE stream, all in-flight handlers are cancelled. This is similar to SignalR's connection-bound lifetime model — but unlike SignalR, there is no per-client concurrency limit like `MaximumParallelInvocationsPerClient`. The GET stream provides cleanup on disconnect, not rate-limiting during the connection.

#### With EventStreamStore

<xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.EventStreamStore> is an advanced API that enables session resumability — storing SSE events so clients can reconnect and replay missed messages using the `Last-Event-ID` header. When configured, handlers gain the ability to call `EnablePollingAsync()`, which closes the POST response early and switches the client to polling mode.

When a handler calls `EnablePollingAsync()`:

- The POST response completes **before the handler finishes**
- The handler continues running in the background, decoupled from any HTTP request
- The client's HTTP/2 stream slot is freed, allowing it to submit more requests
- **HTTP-level backpressure no longer applies** — there is no built-in limit on how many concurrent handlers can accumulate

The `EventStreamStore` itself has TTL-based limits (default: 2-hour event expiration, 30-minute sliding window) that govern event retention, but these do not limit handler concurrency. If you enable `EventStreamStore` on a public-facing server, apply **HTTP rate-limiting middleware** and **reverse proxy limits** to compensate for the loss of stream-level backpressure.

#### With tasks (experimental)

[Tasks](xref:tasks) are an experimental feature that enables a "call-now, fetch-later" pattern for long-running tool calls. When a client sends a task-augmented `tools/call` request, the server creates a task record in the <xref:ModelContextProtocol.IMcpTaskStore>, starts the tool handler as a fire-and-forget background task, and returns the task ID immediately — the POST response completes **before the handler starts its real work**.

This means:

- **No HTTP-level backpressure on task handlers** — each POST returns almost immediately, freeing the stream slot
- A client can rapidly submit many task-augmented requests, each spawning a background handler with no concurrency limit
- Task cleanup is governed by TTL (time-to-live), not by handler completion or session termination

Tasks are a natural fit for **stateless deployments at scale**, where the `IMcpTaskStore` is backed by an external store (database, distributed cache) and the client polls `tasks/get` independently. In this model, work distribution and concurrency control are handled by your infrastructure (job queues, worker pools) rather than by HTTP stream limits.

For servers using the built-in automatic task handlers without external work distribution, apply the same rate-limiting and reverse-proxy protections recommended for `EventStreamStore` deployments.

#### Stateless mode

Stateless mode has the strongest backpressure story. Each handler's lifetime is the HTTP request's lifetime — `McpServer.DisposeAsync()` awaits all in-flight handlers before the POST response completes. This means Kestrel's connection limits, HTTP/2 `MaxStreamsPerConnection`, request timeouts, and rate-limiting middleware all apply naturally — identical to a standard ASP.NET Core minimal API or controller action.

#### Summary

| Configuration | POST held open? | Backpressure mechanism | Concurrent handler limit per connection |
|---|---|---|---|
| **Stateless** | Yes (handler = request) | HTTP/2 streams + Kestrel timeouts | `MaxStreamsPerConnection` (default: 100) |
| **Stateful (default)** | Yes (until handler responds) | HTTP/2 streams | `MaxStreamsPerConnection` (default: 100) |
| **SSE (legacy — opt-in)** | No (returns 202 Accepted) | None built-in; GET stream provides cleanup | Unbounded — apply rate limiting |
| **Stateful + EventStreamStore** | No (if `EnablePollingAsync()` called) | None built-in | Unbounded — apply rate limiting |
| **Stateful + Tasks** | No (returns task ID immediately) | None built-in | Unbounded — apply rate limiting |

### Observability

The SDK automatically integrates with [.NET's OpenTelemetry support](https://learn.microsoft.com/dotnet/core/diagnostics/distributed-tracing) and attaches session metadata to traces and metrics.

#### Activity tags

Every server-side request activity is tagged with `mcp.session.id` — the session's unique identifier. In stateless mode, this tag is `null` because there is no persistent session. Other tags include `mcp.method.name`, `mcp.protocol.version`, `jsonrpc.request.id`, and operation-specific tags like `gen_ai.tool.name` for tool calls.

Use these tags to filter and correlate traces by session in your observability platform (Jaeger, Zipkin, Application Insights, etc.).

#### Metrics

The SDK records histograms under the `Experimental.ModelContextProtocol` meter:

| Metric | Description |
|--------|-------------|
| `mcp.server.session.duration` | Duration of the MCP session on the server |
| `mcp.client.session.duration` | Duration of the MCP session on the client |
| `mcp.server.operation.duration` | Duration of each request/notification on the server |
| `mcp.client.operation.duration` | Duration of each request/notification on the client |

In stateless mode, each HTTP request is its own "session", so `mcp.server.session.duration` measures individual request lifetimes rather than long-lived session durations.

#### Distributed tracing

The SDK propagates [W3C trace context](https://www.w3.org/TR/trace-context/) (`traceparent` / `tracestate`) through JSON-RPC messages via the `_meta` field. This means a client's tool call and the server's handling of that call appear as parent-child spans in a distributed trace, regardless of transport.

## Advanced features

### Session migration

For high-availability deployments, <xref:ModelContextProtocol.AspNetCore.ISessionMigrationHandler> enables session migration across server instances. When a request arrives with a session ID that isn't found locally, the handler is consulted to attempt migration.

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        // Session migration is a stateful-mode feature.
        options.Stateless = false;
        options.SessionMigrationHandler = new MySessionMigrationHandler();
    });
```

You can also register the handler in DI:

```csharp
builder.Services.AddSingleton<ISessionMigrationHandler, MySessionMigrationHandler>();
```

Implementations should:

- Validate that the request is authorized (check `HttpContext.User`)
- Reconstruct the session state from external storage (database, distributed cache, etc.)
- Return `McpServerOptions` pre-populated with `KnownClientInfo` and `KnownClientCapabilities` to skip re-initialization

Session migration adds significant complexity. Consider whether stateless mode is a better fit for your deployment scenario.

### Session resumability

The server can store SSE events for replay when clients reconnect using the `Last-Event-ID` header. Configure this with <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.EventStreamStore>:

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        // Session resumability is a stateful-mode feature.
        options.Stateless = false;
        options.EventStreamStore = new MyEventStreamStore();
    });
```

When configured:

- The server generates unique event IDs for each SSE message
- Events are stored for later replay
- When a client reconnects with `Last-Event-ID`, missed events are replayed before new events are sent

This is useful for clients that may experience transient network issues. Without an event store, clients that disconnect and reconnect may miss events that were sent while they were disconnected.
