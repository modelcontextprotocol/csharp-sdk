---
title: Roots
author: jeffhandley
description: How to provide filesystem roots from clients to MCP servers.
uid: roots
---

## Roots

MCP [roots] allow clients to inform servers about the relevant locations in the filesystem or other hierarchical data sources. Roots are a client-provided feature&mdash;the client declares its root URIs during initialization, and the server can request them to understand the working context.

[roots]: https://modelcontextprotocol.io/specification/2025-11-25/client/roots

### Overview

Roots provide a mechanism for the client to tell the server which directories, projects, or repositories are relevant to the current session. A server might use roots to:

- Scope file searches to the user's project directories
- Understand which repositories are being worked on
- Limit operations to specific filesystem boundaries

Each root is represented by a <xref:ModelContextProtocol.Protocol.Root> with a URI and an optional human-readable name.

### Declaring roots capability on the client

Clients advertise their support for roots in the capabilities sent during initialization. The roots capability is created automatically when a roots handler is provided. Configure the handler through <xref:ModelContextProtocol.Client.McpClientHandlers.RootsHandler>:

```csharp
var options = new McpClientOptions
{
    Handlers = new McpClientHandlers
    {
        RootsHandler = (request, cancellationToken) =>
        {
            return ValueTask.FromResult(new ListRootsResult
            {
                Roots =
                [
                    new Root
                    {
                        Uri = "file:///home/user/projects/my-app",
                        Name = "My Application"
                    },
                    new Root
                    {
                        Uri = "file:///home/user/projects/shared-lib",
                        Name = "Shared Library"
                    }
                ]
            });
        }
    }
};

await using var client = await McpClient.CreateAsync(transport, options);
```

### Requesting roots from the server

Servers can request the client's root list using <xref:ModelContextProtocol.Server.McpServer.RequestRootsAsync*>:

```csharp
[McpServerTool, Description("Lists the user's project roots")]
public static async Task<string> ListProjectRoots(McpServer server, CancellationToken cancellationToken)
{
    var result = await server.RequestRootsAsync(new ListRootsRequestParams(), cancellationToken);

    var summary = new StringBuilder();
    foreach (var root in result.Roots)
    {
        summary.AppendLine($"- {root.Name ?? root.Uri}: {root.Uri}");
    }

    return summary.ToString();
}
```

### Roots change notifications

When the set of roots changes (for example, the user opens a new project), the client notifies the server so it can update its understanding of the working context.

#### Sending change notifications from the client

Roots change notifications are automatically sent when the client's roots handler is updated. However, clients can also send the notification explicitly:

```csharp
await mcpClient.SendNotificationAsync(
    NotificationMethods.RootsListChangedNotification,
    new RootsListChangedNotificationParams());
```

#### Handling change notifications on the server

Servers can register a handler to respond when the client's roots change:

```csharp
server.RegisterNotificationHandler(
    NotificationMethods.RootsListChangedNotification,
    async (notification, cancellationToken) =>
    {
        // Re-request the roots list to get the updated set
        var result = await server.RequestRootsAsync(new ListRootsRequestParams(), cancellationToken);
        Console.WriteLine($"Roots updated. {result.Roots.Count} roots available.");
    });
```
