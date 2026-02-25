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

Use the `ModelContextProtocol.AspNetCore` package to host an MCP server over HTTP. The <xref:Microsoft.AspNetCore.Builder.McpEndpointRouteBuilderExtensions.MapMcp*> method maps the Streamable HTTP endpoint at the specified route (root by default). It also maps legacy SSE endpoints at `{route}/sse` and `{route}/message` for backward compatibility.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<MyTools>();

var app = builder.Build();
app.MapMcp();
app.Run();
```

A custom route can be specified. For example, the [AspNetCoreMcpPerSessionTools] sample uses a route parameter:

[AspNetCoreMcpPerSessionTools]: https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/AspNetCoreMcpPerSessionTools

```csharp
app.MapMcp("/mcp");
```

When using a custom route, Streamable HTTP clients should connect directly to that route (e.g., `https://host/mcp`), while SSE clients should connect to `{route}/sse` (e.g., `https://host/mcp/sse`).

See the `ModelContextProtocol.AspNetCore` package [README](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/src/ModelContextProtocol.AspNetCore/README.md) for more configuration options.

### SSE transport (legacy)

The [SSE (Server-Sent Events)] transport is a legacy mechanism that uses unidirectional server-to-client streaming with a separate HTTP endpoint for client-to-server messages. New implementations should prefer Streamable HTTP.

[SSE (Server-Sent Events)]: https://modelcontextprotocol.io/specification/2024-11-05/basic/transports#http-with-sse

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

The ASP.NET Core integration supports SSE transport alongside Streamable HTTP. The same `MapMcp()` endpoint handles both protocols — clients connecting with SSE are automatically served using the legacy SSE mechanism:

No additional configuration is needed. When a client connects using the SSE protocol, the server responds with an SSE stream for server-to-client messages and accepts client-to-server messages via a separate POST endpoint.

### Transport mode comparison

| Feature | stdio | Streamable HTTP | SSE (Legacy) |
|---------|-------|----------------|--------------|
| Process model | Child process | Remote HTTP | Remote HTTP |
| Direction | Bidirectional | Bidirectional | Server→client stream + client→server POST |
| Session resumption | N/A | ✓ | ✗ |
| Authentication | Process-level | HTTP auth (OAuth, headers) | HTTP auth (OAuth, headers) |
| Best for | Local tools | Remote servers | Legacy compatibility |
