---
title: Transports
author: jeffhandley
description: How to configure stdio, Streamable HTTP, and SSE transports for MCP communication.
uid: transports
---

## Transports

MCP uses a [transport layer] to handle the communication between clients and servers. Three transport mechanisms are supported: **stdio**, **Streamable HTTP**, and **SSE** (Server-Sent Events, legacy).

[transport layer]: https://modelcontextprotocol.io/specification/2025-11-25/basic/transports

### stdio transport

The stdio transport communicates over standard input and output streams. It is best suited for local integrations, as the MCP server runs as a child process of the client.

#### stdio client

Use <xref:ModelContextProtocol.Client.StdioClientTransport> to launch a server process and communicate over its stdin/stdout. This example connects to the [NuGet MCP Server]:

[NuGet MCP Server]: https://learn.microsoft.com/nuget/concepts/nuget-mcp-server

```csharp
var transport = new StdioClientTransport(new StdioClientTransportOptions
{
    Command = "dnx",
    Arguments = ["NuGet.Mcp.Server"],
    ShutdownTimeout = TimeSpan.FromSeconds(10)
});

await using var client = await McpClient.CreateAsync(transport);
```

Key <xref:ModelContextProtocol.Client.StdioClientTransportOptions> properties:

| Property | Description |
|----------|-------------|
| `Command` | The executable to launch (required) |
| `Arguments` | Command-line arguments for the process |
| `WorkingDirectory` | Working directory for the server process |
| `EnvironmentVariables` | Environment variables (merged with current; `null` values remove variables) |
| `ShutdownTimeout` | Graceful shutdown timeout (default: 5 seconds) |
| `StandardErrorLines` | Callback for stderr output from the server process |
| `Name` | Optional transport identifier for logging |

#### stdio server

Use <xref:ModelContextProtocol.Server.StdioServerTransport> for servers that communicate over stdin/stdout:

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<MyTools>();

await builder.Build().RunAsync();
```

### Streamable HTTP transport

The [Streamable HTTP] transport uses HTTP for bidirectional communication with optional streaming. This is the recommended transport for remote servers.

[Streamable HTTP]: https://modelcontextprotocol.io/specification/2025-11-25/basic/transports#streamable-http

#### Streamable HTTP client

Use <xref:ModelContextProtocol.Client.HttpClientTransport> with <xref:ModelContextProtocol.Client.HttpTransportMode.StreamableHttp>:

```csharp
var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri("https://my-mcp-server.example.com/mcp"),
    TransportMode = HttpTransportMode.StreamableHttp,
    ConnectionTimeout = TimeSpan.FromSeconds(30),
    AdditionalHeaders = new Dictionary<string, string>
    {
        ["X-Custom-Header"] = "value"
    }
});

await using var client = await McpClient.CreateAsync(transport);
```

The client also supports automatic transport detection with <xref:ModelContextProtocol.Client.HttpTransportMode.AutoDetect> (the default), which tries Streamable HTTP first and falls back to SSE if the server does not support it:

```csharp
var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri("https://my-mcp-server.example.com/mcp"),
    // TransportMode defaults to AutoDetect
});
```

#### Resuming sessions

Streamable HTTP supports session resumption. Save the session ID, server capabilities, and server info from the original session, then use <xref:ModelContextProtocol.Client.McpClient.ResumeSessionAsync*> to reconnect:

```csharp
var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri("https://my-mcp-server.example.com/mcp"),
    KnownSessionId = previousSessionId
});

await using var client = await McpClient.ResumeSessionAsync(transport, new ResumeClientSessionOptions
{
    ServerCapabilities = previousServerCapabilities,
    ServerInfo = previousServerInfo
});
```

#### Streamable HTTP server (ASP.NET Core)

Use the `ModelContextProtocol.AspNetCore` package to host an MCP server over HTTP. The <xref:Microsoft.AspNetCore.Builder.McpEndpointRouteBuilderExtensions.MapMcp*> method maps the Streamable HTTP endpoint at the specified route (root by default).

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        // Recommended for servers that don't need server-to-client requests.
        options.Stateless = true;
    })
    .WithTools<MyTools>();

var app = builder.Build();
app.MapMcp();
app.Run();
```

By default, the HTTP transport uses **stateful sessions** — the server assigns an `Mcp-Session-Id` to each client and tracks session state in memory. For most servers, **stateless mode is recommended** instead. It simplifies deployment, enables horizontal scaling without session affinity, and avoids issues with clients that don't send the `Mcp-Session-Id` header. We recommend setting `Stateless` explicitly (rather than relying on the current default) for [forward compatibility](xref:stateless#forward-and-backward-compatibility). See [Sessions](xref:stateless) for a detailed guide on when to use stateless vs. stateful mode, configure session options, and understand [cancellation and disposal](xref:stateless#cancellation-and-disposal) behavior during shutdown.

#### Host name validation

If you need to limit which host names the server will respond to, use ASP.NET Core host filtering. Configure `AllowedHosts` in app configuration, such as `appsettings.Development.json` for local loopback values and environment-specific configuration for deployed hosts. See [Host filtering with ASP.NET Core Kestrel web server | Microsoft Learn](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/host-filtering).

```json
// appsettings.Development.json
{
  "AllowedHosts": "localhost;127.0.0.1;[::1]"
}
```

For local development, we recommend keeping `AllowedHosts` limited to loopback values such as `localhost;127.0.0.1;[::1]`.

#### Request origin validation

If you need to limit which browser origins can call the MCP endpoint, use a restrictive ASP.NET Core CORS policy. This is ASP.NET Core's built-in mechanism for validating the browser `Origin` header on cross-origin requests. See [Enable Cross-Origin Requests (CORS) in ASP.NET Core | Microsoft Learn](https://learn.microsoft.com/aspnet/core/security/cors). Only enable CORS if you intentionally want browser-based cross-origin access to this server.

For a **stateless** browser client, a narrowly scoped CORS policy usually only needs the headers the browser would otherwise preflight: `Content-Type` for JSON, `Authorization` when the endpoint is protected, and `MCP-Protocol-Version`. If you enable sessions or resumability, also allow `Mcp-Session-Id` and `Last-Event-ID`, and expose `Mcp-Session-Id` on responses so browser code can read it. `Accept` normally doesn't need to be listed because browsers can already send it without extra CORS configuration.


_In this sample below, the MCP server will allow browser calls from `localhost:5173` where a web application is making the request. In production, this allowed origin list would be configured to the trusted web application domains._

```json
// appsettings.Development.json
{
  "Mcp": {
    "AllowedOrigins": [
      "http://localhost:5173"
    ]
  }
}
```

```csharp
var allowedOrigins = builder.Configuration.GetSection("Mcp:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("McpBrowserClient", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .WithMethods("POST")
            .WithHeaders("Content-Type", "Authorization", "MCP-Protocol-Version")
            .WithExposedHeaders("Mcp-Session-Id");
    });
});

var app = builder.Build();

app.UseCors();
app.MapMcp("/mcp").RequireCors("McpBrowserClient");
```

#### How messages flow

In Streamable HTTP, client requests arrive as HTTP POST requests. The server holds each POST response body open as an SSE stream and writes the JSON-RPC response — plus any intermediate messages like progress notifications or server-to-client requests — back through it. This provides natural HTTP-level backpressure: each POST holds its connection until the handler completes.

In stateful mode, the client can also open a long-lived GET request to receive **unsolicited** messages — notifications or server-to-client requests that the server initiates outside any active request handler (e.g., resource-changed notifications from a background watcher). In stateless mode, the GET endpoint is not mapped, so every message must be part of a POST response. See [How Streamable HTTP delivers messages](xref:stateless#how-streamable-http-delivers-messages) for a detailed breakdown.

A custom route can be specified. For example, the [AspNetCoreMcpPerSessionTools] sample uses a route parameter:

[AspNetCoreMcpPerSessionTools]: https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/AspNetCoreMcpPerSessionTools

```csharp
app.MapMcp("/mcp");
```

When using a custom route, Streamable HTTP clients should connect directly to that route (e.g., `https://host/mcp`), while SSE clients (when [legacy SSE is enabled](xref:stateless#legacy-sse-transport)) should connect to `{route}/sse` (e.g., `https://host/mcp/sse`).

### SSE transport (legacy)

The [SSE (Server-Sent Events)] transport is a legacy mechanism that uses unidirectional server-to-client streaming with a separate HTTP endpoint for client-to-server messages. New implementations should prefer Streamable HTTP.

[SSE (Server-Sent Events)]: https://modelcontextprotocol.io/specification/2024-11-05/basic/transports#http-with-sse

<!-- mlc-disable-next-line -->
> [!NOTE]
> The SSE transport is considered legacy. The [Streamable HTTP](#streamable-http-transport) transport is the recommended approach for HTTP-based communication and supports bidirectional streaming.

#### SSE client

Use <xref:ModelContextProtocol.Client.HttpClientTransport> with <xref:ModelContextProtocol.Client.HttpTransportMode.Sse>:

```csharp
var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri("https://my-mcp-server.example.com/sse"),
    TransportMode = HttpTransportMode.Sse,
    MaxReconnectionAttempts = 5,
    DefaultReconnectionInterval = TimeSpan.FromSeconds(1)
});

await using var client = await McpClient.CreateAsync(transport);
```

SSE-specific configuration options:

| Property | Description |
|----------|-------------|
| `MaxReconnectionAttempts` | Maximum number of reconnection attempts on stream disconnect (default: 5) |
| `DefaultReconnectionInterval` | Wait time between reconnection attempts (default: 1 second) |

#### SSE server (ASP.NET Core)

The ASP.NET Core integration supports SSE transport alongside Streamable HTTP. Legacy SSE endpoints (`/sse` and `/message`) are **disabled by default** and <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.EnableLegacySse> is marked `[Obsolete]` (diagnostic `MCP9004`). SSE always requires stateful mode; legacy SSE endpoints are never mapped when `Stateless = true`.

**Why SSE is disabled by default.** The SSE transport separates request and response channels: clients POST JSON-RPC messages to `/message` and receive all responses through a long-lived GET SSE stream on `/sse`. Because the POST endpoint returns `202 Accepted` immediately — before the handler even runs — there is **no HTTP-level backpressure** on handler concurrency. A client (or attacker) can flood the server with tool calls without waiting for prior requests to complete. In contrast, Streamable HTTP holds each POST response open until the handler finishes, providing natural backpressure. See [Request backpressure](xref:stateless#request-backpressure) for a detailed comparison and mitigations if you must use SSE.

To enable legacy SSE, set `EnableLegacySse` to `true`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        // SSE requires stateful mode (the default). Set explicitly for forward compatibility.
        options.Stateless = false;

#pragma warning disable MCP9004 // EnableLegacySse is obsolete
        // Enable legacy SSE endpoints for clients that don't support Streamable HTTP.
        // See sessions doc for backpressure implications.
        options.EnableLegacySse = true;
#pragma warning restore MCP9004
    })
    .WithTools<MyTools>();

var app = builder.Build();

// MapMcp() serves Streamable HTTP. Legacy SSE (/sse and /message) is also
// available because EnableLegacySse is set to true above.
app.MapMcp();
app.Run();
```

See [Sessions — Legacy SSE transport](xref:stateless#legacy-sse-transport) for details on SSE session lifetime and configuration.

### Transport mode comparison

| Feature | stdio | Streamable HTTP (stateless) | Streamable HTTP (stateful) | SSE (legacy, stateful) |
|---------|-------|-----------------------------|----------------------------|--------------|
| Process model | Child process | Remote HTTP | Remote HTTP | Remote HTTP |
| Direction | Bidirectional | Request-response | Bidirectional | Server→client stream + client→server POST |
| Sessions | Implicit (one per process) | None — each request is independent | `Mcp-Session-Id` tracked in memory | Session ID via query string, tracked in memory |
| Server-to-client requests | ✓ | ✗ (see [MRTR proposal](https://github.com/modelcontextprotocol/csharp-sdk/pull/1458)) | ✓ | ✓ |
| Unsolicited notifications | ✓ | ✗ | ✓ | ✓ |
| Backpressure | Implicit (stdin/stdout flow control) | ✓ (POST held open until handler completes) | ✓ (POST held open until handler completes) | ✗ (POST returns 202 immediately — see [backpressure](xref:stateless#request-backpressure)) |
| Session resumption | N/A | N/A | ✓ | ✗ |
| Horizontal scaling | N/A | No constraints | Requires session affinity | Requires session affinity |
| Authentication | Process-level | HTTP auth (OAuth, headers) | HTTP auth (OAuth, headers) | HTTP auth (OAuth, headers) |
| Best for | Local tools, IDE integrations | Remote servers, production deployments | Local HTTP debugging, server-to-client features | Legacy client compatibility |

For a detailed comparison of stateless vs. stateful mode — including deployment trade-offs, security considerations, and configuration — see [Sessions](xref:stateless).

### In-memory transport

The <xref:ModelContextProtocol.Server.StreamServerTransport> and <xref:ModelContextProtocol.Protocol.StreamClientTransport> types work with any `Stream`, including in-memory pipes. This is useful for testing, embedding an MCP server in a larger application, or running a client and server in the same process without network overhead.

The following example creates a client and server connected via `System.IO.Pipelines` (from the [InMemoryTransport sample](https://github.com/modelcontextprotocol/csharp-sdk/blob/51a4fde4d9cfa12ef9430deef7daeaac36625be8/samples/InMemoryTransport/Program.cs)):

```csharp
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.IO.Pipelines;

Pipe clientToServerPipe = new(), serverToClientPipe = new();

// Create a server using a stream-based transport over an in-memory pipe.
await using McpServer server = McpServer.Create(
    new StreamServerTransport(clientToServerPipe.Reader.AsStream(), serverToClientPipe.Writer.AsStream()),
    new McpServerOptions
    {
        ToolCollection = [McpServerTool.Create((string message) => $"Echo: {message}", new() { Name = "echo" })]
    });
_ = server.RunAsync();

// Connect a client using a stream-based transport over the same in-memory pipe.
await using McpClient client = await McpClient.CreateAsync(
    new StreamClientTransport(clientToServerPipe.Writer.AsStream(), serverToClientPipe.Reader.AsStream()));

// List and invoke tools.
var tools = await client.ListToolsAsync();
var echo = tools.First(t => t.Name == "echo");
Console.WriteLine(await echo.InvokeAsync(new() { ["arg"] = "Hello World" }));
```

Like [stdio](#stdio-transport), the in-memory transport is inherently single-session — there is no `Mcp-Session-Id` header, and server-to-client requests (sampling, elicitation, roots) work naturally over the bidirectional pipe. This makes it ideal for testing servers that depend on these features. See [Sessions](xref:stateless) for how session behavior varies across transports.
