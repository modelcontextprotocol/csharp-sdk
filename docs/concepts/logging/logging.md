---
title: Logging
author: mikekistler
description: How to use the logging feature in the MCP C# SDK.
uid: logging
---

## Logging

MCP servers may expose log messages to clients through the [Logging utility].

[Logging utility]: https://modelcontextprotocol.io/specification/2025-06-18/server/utilities/logging

This document describes how to implement logging in MCP servers and how clients can consume log messages.

### Logging Levels

MCP uses the logging levels defined in [RFC 5424](https://tools.ietf.org/html/rfc5424).

The MCP C# SDK uses the standard .NET [ILogger] and [ILoggerProvider] abstractions, which support a slightly
different set of logging levels. Here's the levels and how they map to standard .NET logging levels.

| Level     | .NET | Description                       | Example Use Case             |
|-----------|------|-----------------------------------|------------------------------|
| debug     | ✓    | Detailed debugging information    | Function entry/exit points   |
| info      | ✓    | General informational messages    | Operation progress updates   |
| notice    |      | Normal but significant events     | Configuration changes        |
| warning   | ✓    | Warning conditions                | Deprecated feature usage     |
| error     | ✓    | Error conditions                  | Operation failures           |
| critical  | ✓    | Critical conditions               | System component failures    |
| alert     |      | Action must be taken immediately  | Data corruption detected     |
| emergency |      | System is unusable                |                              |

**Note:** .NET's [ILogger] also supports a `Trace` level (more verbose than Debug) log level.
As there is no equivalent level in the MCP logging levels, Trace level logs messages are silently
dropped when sending messages to the client.

[ILogger]: https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.ilogger
[ILoggerProvider]: https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.iloggerprovider

### Server configuration and logging

MCP servers that implement the Logging utility must declare this in the capabilities sent in the
[Initialization] phase at the beginning of the MCP session.

[Initialization]: https://modelcontextprotocol.io/specification/2025-06-18/basic/lifecycle#initialization

Servers built with the C# SDK always declare the logging capability. Doing so does not obligate the server
to send log messages -- only allows it. Note that stateless MCP servers may not be capable of sending log
messages as there may not be an open connection to the client on which the log messages could be sent.

The C# SDK provides an extension method [WithSetLoggingLevelHandler] on [IMcpServerBuilder] to allow the
server to perform any special logic it wants to perform when a client sets the logging level. However, the
SDK already takes care of setting the [LoggingLevel] in the [IMcpServer], so most servers will not need to
implement this.

[IMcpServer]: https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.Server.IMcpServer.html
[IMcpServerBuilder]: https://modelcontextprotocol.github.io/csharp-sdk/api/Microsoft.Extensions.DependencyInjection.IMcpServerBuilder.html
[WithSetLoggingLevelHandler]: https://modelcontextprotocol.github.io/csharp-sdk/api/Microsoft.Extensions.DependencyInjection.McpServerBuilderExtensions.html#Microsoft_Extensions_DependencyInjection_McpServerBuilderExtensions_WithSetLoggingLevelHandler_Microsoft_Extensions_DependencyInjection_IMcpServerBuilder_System_Func_ModelContextProtocol_Server_RequestContext_ModelContextProtocol_Protocol_SetLevelRequestParams__System_Threading_CancellationToken_System_Threading_Tasks_ValueTask_ModelContextProtocol_Protocol_EmptyResult___
[LoggingLevel]: https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.Server.IMcpServer.html#ModelContextProtocol_Server_IMcpServer_LoggingLevel

MCP Servers using the MCP C# SDK can obtain an [ILoggerProvider] from the IMcpServer [AsClientLoggerProvider] extension method,
and from that can create an [ILogger] instance for logging messages that should be sent to the MCP client.

[!code-csharp[](samples/server/Tools/LoggingTools.cs?name=snippet_LoggingConfiguration)]

[ILoggerProvider]: https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.iloggerprovider
[AsClientLoggerProvider]: https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.Server.McpServerExtensions.html#ModelContextProtocol_Server_McpServerExtensions_AsClientLoggerProvider_ModelContextProtocol_Server_IMcpServer_
[ILogger]: https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.ilogger

### Client support for logging

When the server indicates that it supports logging, clients should configure
the logging level to specify which messages the server should send to the client.

Clients should check if the server supports logging by checking the [Logging] property of the [ServerCapabilities] field of [IMcpClient].

[IMcpClient]: https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.Client.IMcpClient.html
[ServerCapabilities]: https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.Client.IMcpClient.html#ModelContextProtocol_Client_IMcpClient_ServerCapabilities
[Logging]: https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.Protocol.ServerCapabilities.html#ModelContextProtocol_Protocol_ServerCapabilities_Logging

[!code-csharp[](samples/client/Program.cs?name=snippet_LoggingCapabilities)]

If the server supports logging, the client should set the level of log messages it wishes to receive with
the [SetLoggingLevel] method on [IMcpClient]. If the client does not set a logging level, the server might choose
to send all log messages or none -- this is not specified in the protocol -- so it is important that the client
sets a logging level to ensure it receives the desired log messages and only those messages.

The `loggingLevel` set by the client is an MCP logging level.
See the [Logging Levels](#logging-levels) section above for the mapping between MCP and .NET logging levels.

[SetLoggingLevel]: https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.Client.McpClientExtensions.html#ModelContextProtocol_Client_McpClientExtensions_SetLoggingLevel_ModelContextProtocol_Client_IMcpClient_Microsoft_Extensions_Logging_LogLevel_System_Threading_CancellationToken_

[!code-csharp[](samples/client/Program.cs?name=snippet_LoggingLevel)]

Lastly, the client must configure a notification handler for [NotificationMethods.LoggingMessageNotification] notifications.
The following example simply writes the log messages to the console.

[NotificationMethods.LoggingMessageNotification]: https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.Protocol.NotificationMethods.html#ModelContextProtocol_Protocol_NotificationMethods_LoggingMessageNotification

[!code-csharp[](samples/client/Program.cs?name=snippet_LoggingHandler)]
