---
title: Progress
author: mikekistler
description:
uid: progress
---

## Progress

The Model Context Protocol (MCP) supports [progress tracking] for long-running operations through notification messages.

[progress tracking]: https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/progress

Typically progress tracking is supported by server tools that perform operations that take a significant amount of time to complete, such as image generation or complex calculations.
However, progress tracking is defined in the MCP specification as a general feature that can be implemented for any request that's handled by either a server or a client.
This project illustrates the common case of a server tool that performs a long-running operation and sends progress updates to the client.

### Server Implementation

When processing a request, the server can use the <xref:ModelContextProtocol.McpSession.SendNotificationAsync*> extension method of <xref:ModelContextProtocol.Server.McpServer> to send progress updates,
specifying `"notifications/progress"` as the notification method name.
The C# SDK registers an instance of <xref:ModelContextProtocol.Server.McpServer> with the dependency injection container,
so tools can simply add a parameter of type <xref:ModelContextProtocol.Server.McpServer> to their method signature to access it.
The parameters passed to <xref:ModelContextProtocol.McpSession.SendNotificationAsync*> should be an instance of <xref:ModelContextProtocol.Protocol.ProgressNotificationParams>, which includes the current progress, total steps, and an optional message.

The server must verify that the caller provided a `progressToken` in the request and include it in the call to <xref:ModelContextProtocol.McpSession.SendNotificationAsync*>. The following example demonstrates how a server can send a progress notification:

[!code-csharp[](samples/server/Tools/LongRunningTools.cs?name=snippet_SendProgress)]

### Client Implementation

Clients request progress updates by including a `progressToken` in the parameters of a request.
Note that servers aren't required to support progress tracking, so clients should not depend on receiving progress updates.

In the MCP C# SDK, clients can specify a `progressToken` in the request parameters when calling a tool method.
The client should also provide a notification handler to process "notifications/progress" notifications.
There are two ways to do this. The first is to register a notification handler using the <xref:ModelContextProtocol.McpSession.RegisterNotificationHandler*> method on the <xref:ModelContextProtocol.Client.McpClient> instance. A handler registered this way will receive all progress notifications sent by the server.

```csharp
await using var handler = mcpClient.RegisterNotificationHandler(NotificationMethods.ProgressNotification,
    (notification, cancellationToken) =>
    {
        if (JsonSerializer.Deserialize<ProgressNotificationParams>(notification.Params) is { } pn &&
            pn.ProgressToken == progressToken)
        {
            // progress.Report(pn.Progress);
            Console.WriteLine($"Tool progress: {pn.Progress.Progress} of {pn.Progress.Total} - {pn.Progress.Message}");
        }
        return ValueTask.CompletedTask;
    });
```

The second way is to pass a [`Progress<T>`](https://learn.microsoft.com/dotnet/api/system.progress-1) instance to the tool method. `Progress<T>` is a standard .NET type that provides a way to receive progress updates.
For the purposes of MCP progress notifications, `T` should be <xref:ModelContextProtocol.ProgressNotificationValue>.
The MCP C# SDK will automatically handle progress notifications and report them through the `Progress<T>` instance.
This notification handler will only receive progress updates for the specific request that was made,
rather than all progress notifications from the server.

[!code-csharp[](samples/client/Program.cs?name=snippet_ProgressHandler)]
