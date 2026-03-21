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

### Multi Round-Trip Requests (MRTR)

When both the client and server opt in to the experimental [MRTR](xref:mrtr) protocol, root list requests are handled via incomplete result / retry instead of a direct JSON-RPC request. This is transparent — the existing `RequestRootsAsync` API works identically regardless of whether MRTR is active.

#### High-level API

No code changes are needed. `RequestRootsAsync` automatically uses MRTR when both sides have opted in:

```csharp
// This code works the same with or without MRTR — the SDK handles it transparently.
var result = await server.RequestRootsAsync(new ListRootsRequestParams(), cancellationToken);
foreach (var root in result.Roots)
{
    Console.WriteLine($"Root: {root.Name ?? root.Uri}");
}
```

#### Low-level API

For stateless servers or scenarios requiring manual control, throw <xref:ModelContextProtocol.Protocol.IncompleteResultException> with a roots input request. On retry, read the client's response from <xref:ModelContextProtocol.Protocol.RequestParams.InputResponses>:

```csharp
[McpServerTool, Description("Tool that requests roots via low-level MRTR")]
public static string ListRootsWithMrtr(
    McpServer server,
    RequestContext<CallToolRequestParams> context)
{
    // On retry, process the client's roots response
    if (context.Params!.InputResponses?.TryGetValue("get_roots", out var response) is true)
    {
        var roots = response.RootsResult?.Roots ?? [];
        return $"Found {roots.Count} roots: {string.Join(", ", roots.Select(r => r.Uri))}";
    }

    if (!server.IsMrtrSupported)
    {
        return "This tool requires MRTR support.";
    }

    // First call — request the client's root list
    throw new IncompleteResultException(
        inputRequests: new Dictionary<string, InputRequest>
        {
            ["get_roots"] = InputRequest.ForRootsList(new ListRootsRequestParams())
        },
        requestState: "awaiting-roots");
}
```

> [!TIP]
> See [Multi Round-Trip Requests (MRTR)](xref:mrtr) for the full protocol details, including load shedding, multiple round trips, and the compatibility matrix.
