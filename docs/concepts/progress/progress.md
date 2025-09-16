---
title: Progress
author: mikekistler
description:
uid: progress
---

## Progress

The Model Context Protocol (MCP) supports [progress tracking] for long-running operations through notification messages.

[progress tracking]: https://modelcontextprotocol.io/specification/2025-06-18/basic/utilities/progress

Typically progress tracking is supported by server tools that perform operations that take a significant amount of time to complete, such as image generation or complex calculations.
However, progress tracking is defined in the MCP specification as a general feature that can be implemented for any request that is handled by either a server or a client.
This project illustrates the common case of a server tool that performs a long-running operation and sends progress updates to the client.

### Server Implementation

When processing a request, the server can use the [sendNotificationAsync] extension method of [IMcpServer] to send progress updates,
specifying `"notifications/progress"` as the notification method name.
The C# SDK registers an instance of [IMcpServer] with the dependency injection container,
so tools can simply add a parameter of type [IMcpServer] to their method signature to access it.
The parameters passed to [sendNotificationAsync] should be an instance of [ProgressNotificationParams], which includes the current progress, total steps, and an optional message.

[sendNotificationAsync]: https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.McpEndpointExtensions.html#ModelContextProtocol_McpEndpointExtensions_SendNotificationAsync_ModelContextProtocol_IMcpEndpoint_System_String_System_Threading_CancellationToken_
[IMcpServer]: https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.Server.IMcpServer.html
[ProgressNotificationParams]: https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.Protocol.ProgressNotificationParams.html

The server must verify that the caller provided a `progressToken` in the request and include it in the call to [sendNotificationAsync]. The following example demonstrates how a server can send a progress notification:

[!code-csharp[](samples/server/Tools/LongRunningTools.cs?name=snippet_SendProgress)]

### Client Implementation

Clients request progress updates by including a `progressToken` in the parameters of a request.
Note that servers are not required to support progress tracking, so clients should not depend on receiving progress updates.

In the MCP C# SDK, clients can specify a `progressToken` in the request parameters when calling a tool method.
The client should also provide a notification handler to process "notifications/progress" notifications.
There are two way to do this. The first is to register a notification handler using the [RegisterNotificationHandler] method on the [IMcpClient] instance. A handler registered this way will receive all progress notifications sent by the server.

[IMcpClient]: https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.Client.IMcpClient.html
[RegisterNotificationHandler]: https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.IMcpEndpoint.html#ModelContextProtocol_IMcpEndpoint_RegisterNotificationHandler_System_String_System_Func_ModelContextProtocol_Protocol_JsonRpcNotification_System_Threading_CancellationToken_System_Threading_Tasks_ValueTask__

```csharp
mcpClient.RegisterNotificationHandler(NotificationMethods.ProgressNotification,
    (notification, cancellationToken) =>
    {
        if (JsonSerializer.Deserialize<ProgressNotificationParams>(notification.Params) is { } pn &&
            pn.ProgressToken == progressToken)
        {
            // progress.Report(pn.Progress);
            Console.WriteLine($"Tool progress: {pn.Progress.Progress} of {pn.Progress.Total} - {pn.Progress.Message}");
        }
        return ValueTask.CompletedTask;
    }).ConfigureAwait(false);
```

The second way is to pass a [Progress`<T>`] instance to the tool method. [Progress`<T>`] is a standard .NET type that provides a way to receive progress updates.
For the purposes of MCP progress notifications, `T` should be [ProgressNotificationValue].
The MCP C# SDK will automatically handle progress notifications and report them through the [Progress`<T>`] instance.
This notification handler will only receive progress updates for the specific request that was made,
rather than all progress notifications from the server.

[Progress`<T>`]: https://learn.microsoft.com/en-us/dotnet/api/system.progress-1
[ProgressNotificationValue]: https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.ProgressNotificationValue.html

[!code-csharp[](samples/client/Program.cs?name=snippet_ProgressHandler)]
