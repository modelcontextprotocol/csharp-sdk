---
title: Ping
author: jeffhandley
description: How to use the MCP ping mechanism to check connection health.
uid: ping
---

## Ping

MCP includes a [ping mechanism] that allows either side of a connection to verify that the other side is still responsive. This is useful for connection health monitoring and keep-alive scenarios.

[ping mechanism]: https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/ping

### Pinging from the client

Use the <xref:ModelContextProtocol.Client.McpClient.PingAsync*> method to verify the server is responsive:

```csharp
await client.PingAsync(cancellationToken: cancellationToken);
```

### Automatic ping handling

Incoming ping requests from either side are responded to automatically. No additional configuration is needed&mdash;when a ping request is received, a ping response is sent immediately.
