---
title: Ping
author: jeffhandley
description: How to use the MCP ping mechanism to check connection health.
uid: ping
---

## Ping

MCP includes a [ping mechanism] that allows either side of a connection to verify that the other side is still responsive. This is useful for connection health monitoring and keep-alive scenarios.

[ping mechanism]: https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/ping

### Overview

The ping operation is a simple request/response exchange. Either the client or the server can initiate a ping, and the other side responds automatically. Ping responses are handled automatically&mdash;callers only need to invoke the method to send a ping.

### Pinging from the client

Use the <xref:ModelContextProtocol.Client.McpClient.PingAsync*> method to verify the server is responsive:

```csharp
await using var client = await McpClient.CreateAsync(transport);

try
{
    await client.PingAsync();
    Console.WriteLine("Server is responsive.");
}
catch (OperationCanceledException)
{
    Console.WriteLine("Ping timed out - server may be unresponsive.");
}
catch (McpException ex)
{
    Console.WriteLine($"Ping failed: {ex.Message}");
}
```

A timeout can also be specified using a `CancellationToken`:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

try
{
    await client.PingAsync(cancellationToken: cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Server did not respond within 5 seconds.");
}
```

### Automatic ping handling

Incoming ping requests from either side are responded to automatically. No additional configuration is needed&mdash;when a ping request is received, a ping response is sent immediately.
