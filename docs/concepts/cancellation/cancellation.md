---
title: Cancellation
author: jeffhandley
description: How to cancel in-flight MCP requests using cancellation tokens and notifications.
uid: cancellation
---

## Cancellation

MCP supports [cancellation] of in-flight requests. Either side can cancel a previously issued request by using .NET's [task cancellation] with a `CancellationToken` to send a cancellation notification.

[cancellation]: https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/cancellation
[task cancellation]: https://learn.microsoft.com/dotnet/standard/parallel-programming/task-cancellation

### Overview

When a client cancels a pending request, a `notifications/cancelled` notification is sent to the server. The server's `CancellationToken` for that request is then triggered, allowing the server-side handler to stop work gracefully. This same mechanism works in reverse for server-to-client requests.

### Client-side cancellation

All client methods accept a `CancellationToken` parameter. Cancel a pending request by canceling the token:

```csharp
using var cts = new CancellationTokenSource();

// Start a long-running tool call
var toolTask = client.CallToolAsync(
    "long_running_operation",
    new Dictionary<string, object?> { ["duration"] = 60 },
    cancellationToken: cts.Token);

// Cancel after 5 seconds
cts.CancelAfter(TimeSpan.FromSeconds(5));

try
{
    var result = await toolTask;
}
catch (OperationCanceledException)
{
    Console.WriteLine("Tool call was cancelled.");
}
```

When the `CancellationToken` is cancelled, a `notifications/cancelled` notification is automatically sent to the server with the request ID, allowing the server to stop processing.

### Server-side cancellation handling

Server tool methods should accept a `CancellationToken` parameter and check it during long-running operations:

```csharp
[McpServerTool, Description("A long-running computation")]
public static async Task<string> LongComputation(
    [Description("Number of iterations")] int iterations,
    CancellationToken cancellationToken)
{
    for (int i = 0; i < iterations; i++)
    {
        // Check for cancellation on each iteration
        cancellationToken.ThrowIfCancellationRequested();

        await Task.Delay(1000, cancellationToken);
    }

    return $"Completed {iterations} iterations.";
}
```

When the client sends a cancellation notification, the `CancellationToken` provided to the tool method is automatically triggered. The `OperationCanceledException` propagates back to the client as a cancellation response.

### Cancellation notification details

The cancellation notification includes:

- **RequestId**: The ID of the request to cancel, allowing the receiver to correlate the cancellation with the correct in-flight request.
- **Reason**: An optional human-readable reason for the cancellation.

```csharp
// This is sent automatically when a CancellationToken is cancelled,
// but you can also observe cancellation notifications:
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
