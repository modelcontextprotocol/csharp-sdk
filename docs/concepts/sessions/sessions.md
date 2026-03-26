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
- The `GET` and `DELETE` MCP endpoints are not mapped, and the legacy `/sse` endpoint is disabled
- **Server-to-client requests are disabled**, including:
  - [Sampling](xref:sampling) (`SampleAsync`)
  - [Elicitation](xref:elicitation) (`ElicitAsync`)
  - [Roots](xref:roots) (`RequestRootsAsync`)
- Unsolicited server-to-client notifications (e.g., resource update notifications, logging messages) are not supported

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
- Session-scoped state (e.g., per-session DI scopes, RunSessionHandler)

### When to use stateful mode

Use stateful mode when your server needs one or more of:

- **Server-to-client requests**: Tools that call `ElicitAsync`, `SampleAsync`, or `RequestRootsAsync` to interact with the client
- **Unsolicited notifications**: Sending resource-changed notifications or log messages without a preceding client request
- **Resource subscriptions**: Clients subscribing to resource changes and receiving updates
- **Session-scoped state**: Logic that must persist across multiple requests within the same session
- **Debugging stdio servers over HTTP**: When you want to test a typically stateful stdio server over HTTP while supporting concurrent connections from editors like Claude Code, GitHub Copilot in VS Code, Cursor, etc., sessions let you distinguish between them

### Deployment footguns

Stateful sessions introduce several challenges that you should carefully consider:

#### Session affinity required

All requests for a given session must reach the same server instance, because sessions live in memory. If you deploy behind a load balancer, you must configure session affinity (sticky sessions) to route requests to the correct instance. Without session affinity, clients will receive `404 Session not found` errors.

#### Memory consumption

Each session consumes memory on the server for the lifetime of the session. The default idle timeout is **2 hours**, and the default maximum idle session count is **10,000**. A server with many concurrent clients can accumulate significant memory usage. Monitor your idle session count and tune <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.IdleTimeout> and <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.MaxIdleSessionCount> to match your workload.

#### Server restarts lose all sessions

Sessions are stored in memory by default. When the server restarts (for deployments, crashes, or scaling events), all sessions are lost. Clients must reinitialize their sessions, which some clients may not handle gracefully.

You can mitigate this with <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.SessionMigrationHandler>, but this adds complexity. See [Session migration](#session-migration) for details.

#### Clients that don't send Mcp-Session-Id

Some MCP clients may not send the `Mcp-Session-Id` header on every request. When this happens, the server responds with an error: `"Bad Request: A new session can only be created by an initialize request."` This can happen after a server restart, when a client loses its session ID, or when a client simply doesn't support sessions. If you see this error, consider whether your server actually needs sessions — and if not, switch to stateless mode.

#### No built-in backpressure on request handlers

The SDK does not limit how long a handler can run or how many requests can be processed concurrently within a session. A misbehaving or compromised client can flood a stateful session with requests, and each request will spawn a handler that runs to completion. This can lead to thread starvation, GC pressure, or out-of-memory conditions that affect the entire HTTP server process — not just the offending session.

Stateless mode is significantly more resilient here because each tool call is a standard HTTP request-response. This means Kestrel and IIS connection limits, request timeouts, and rate-limiting middleware all apply naturally. The <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.IdleTimeout> and <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.MaxIdleSessionCount> settings help protect against non-malicious overuse (e.g., a buggy client creating too many sessions), but they are not a substitute for HTTP-level protections.

## stdio transport

The [stdio transport](xref:transports) is inherently single-session. The client launches the server as a child process and communicates over stdin/stdout. There is exactly one session per process, the session starts when the process starts, and it ends when the process exits.

Because there is only one connection, stdio servers don't need session IDs or any explicit session management. The session is implicit in the process boundary. This makes stdio the simplest transport to use, and it naturally supports all server-to-client features (sampling, elicitation, roots) because there is always exactly one client connected.

However, stdio servers cannot be shared between multiple clients. Each client needs its own server process. This is fine for local tool integrations (IDEs, CLI tools) but not suitable for remote or multi-tenant scenarios — use [Streamable HTTP](xref:transports) for those.

## Session lifecycle (HTTP)

### Creation

A session begins when a client sends an `initialize` JSON-RPC request without an `Mcp-Session-Id` header. The server:

1. Creates a new session with a unique session ID
2. Calls <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.ConfigureSessionOptions> (if configured) to customize the session's `McpServerOptions`
3. Starts the MCP server for the session
4. Returns the session ID in the `Mcp-Session-Id` response header along with the `InitializeResult`

All subsequent requests from the client must include this session ID.

### Activity tracking

The server tracks the last activity time for each session. Activity is recorded when:

- A request arrives for the session (POST, GET, or DELETE)
- A response is sent for the session

### Idle timeout

Sessions that have no activity for the duration of <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.IdleTimeout> (default: **2 hours**) are automatically closed. The idle timeout is checked in the background every 5 seconds.

A client can keep its session alive by maintaining an open `GET` request (SSE stream). Sessions with an active `GET` request are never considered idle.

When a session times out:

- The session's `McpServer` is disposed
- Any pending requests receive cancellation
- A client trying to use the expired session ID receives a `404 Session not found` error and should start a new session

You can disable idle timeout by setting it to `Timeout.InfiniteTimeSpan`, though this is not recommended for production deployments.

### Maximum idle session count

<xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.MaxIdleSessionCount> (default: **10,000**) limits how many idle sessions can exist simultaneously. If this limit is exceeded:

- A critical error is logged
- The oldest idle sessions are terminated (even if they haven't reached their idle timeout)
- Termination continues until the idle count is back below the limit

Sessions with an active `GET` request (open SSE stream) don't count toward this limit.

### Termination

Sessions can be terminated by:

- **Client DELETE request**: The client sends an HTTP `DELETE` to the session endpoint with its `Mcp-Session-Id`
- **Idle timeout**: The session exceeds the idle timeout without activity
- **Max idle count**: The server exceeds its maximum idle session count and prunes the oldest sessions
- **Server shutdown**: All sessions are disposed when the server shuts down

## Configuration reference

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

## Per-session configuration

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

### Per-request configuration in stateless mode

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

## User binding

When authentication is configured, the server automatically binds sessions to the authenticated user. This prevents one user from hijacking another user's session.

### How it works

1. When a session is created, the server captures the authenticated user's identity from `HttpContext.User`
2. The server extracts a user ID claim in priority order:
   - `ClaimTypes.NameIdentifier` (`http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`)
   - `"sub"` (OpenID Connect subject claim)
   - `ClaimTypes.Upn` (`http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn`)
3. On each subsequent request, the server validates that the current user matches the session's original user
4. If there's a mismatch, the server responds with `403 Forbidden`

This binding is automatic — no configuration is needed. If no authentication middleware is configured, user binding is skipped (the session is not bound to any user).

## Session migration

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

## Session resumability

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

## Choosing stateless vs. stateful

| Consideration | Stateless | Stateful |
|---|---|---|
| **Deployment** | Any topology — load balancer, serverless, multi-instance | Requires session affinity (sticky sessions) |
| **Scaling** | Horizontal scaling without constraints | Limited by session-affinity routing |
| **Server restarts** | No impact — each request is independent | All sessions lost; clients must reinitialize |
| **Memory** | Per-request only | Per-session (default: up to 10,000 sessions × 2 hours) |
| **Server-to-client requests** | Not supported (see [MRTR proposal](https://github.com/modelcontextprotocol/csharp-sdk/pull/1458) for a stateless alternative) | Supported (sampling, elicitation, roots) |
| **Unsolicited notifications** | Not supported | Supported (resource updates, logging) |
| **Resource subscriptions** | Not supported | Supported |
| **Client compatibility** | Works with all clients | Requires clients to track and send `Mcp-Session-Id` |
