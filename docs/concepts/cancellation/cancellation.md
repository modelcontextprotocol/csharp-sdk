---
title: Cancellation
author: jeffhandley
description: How to cancel in-flight MCP requests using cancellation tokens and notifications.
uid: cancellation
---

## Cancellation

MCP supports [cancellation] of in-flight requests. Either side can cancel a previously issued request, and `CancellationToken` parameters on MCP methods are wired to send and receive `notifications/cancelled` notifications over the protocol.

[cancellation]: https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/cancellation
[task cancellation]: https://learn.microsoft.com/dotnet/standard/parallel-programming/task-cancellation

### How cancellation maps to MCP notifications

When a `CancellationToken` passed to a client method (such as <xref:ModelContextProtocol.Client.McpClient.CallToolAsync*>) is cancelled, a `notifications/cancelled` notification is sent to the server with the request ID. On the server side, the `CancellationToken` provided to the tool method is then triggered, allowing the handler to stop work gracefully. This same mechanism works in reverse for server-to-client requests.

### Server-side cancellation handling

Server tool methods receive a `CancellationToken` that is triggered when the client sends a cancellation notification. Pass this token through to any async operations so they stop promptly:

```csharp
[McpServerTool, Description("A long-running computation")]
public static async Task<string> LongComputation(
    [Description("Number of iterations")] int iterations,
    CancellationToken cancellationToken)
{
    for (int i = 0; i < iterations; i++)
    {
        await Task.Delay(1000, cancellationToken);
    }

    return $"Completed {iterations} iterations.";
}
```

When the client sends a cancellation notification, the `OperationCanceledException` propagates back to the client as a cancellation response.

### Cancellation notification details

The cancellation notification includes:

- **RequestId**: The ID of the request to cancel, allowing the receiver to correlate the cancellation with the correct in-flight request.
- **Reason**: An optional human-readable reason for the cancellation.

Cancellation notifications can be observed by registering a handler. For broader interception of notifications and other messages, <xref:ModelContextProtocol.Server.McpMessageFilter> delegates can be added to the <xref:ModelContextProtocol.Server.McpMessageFilters.IncomingFilters> collection in <xref:ModelContextProtocol.Server.McpServerOptions.Filters>.

```csharp
mcpClient.RegisterNotificationHandler(
    NotificationMethods.CancelledNotification,
    (notification, ct) =>
    {
        var cancelled = notification.Params?.Deserialize<CancelledNotificationParams>(
            McpJsonUtilities.DefaultOptions);
        if (cancelled is not null)
        {
            Console.WriteLine($"Request {cancelled.RequestId} cancelled: {cancelled.Reason}");
        }
        return default;
    });
```
