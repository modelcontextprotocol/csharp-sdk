---
title: Logging
author: mikekistler
description: How to use the logging feature in the MCP C# SDK.
uid: logging
---

## Logging

MCP servers can expose log messages to clients through the [Logging utility].

[Logging utility]: https://modelcontextprotocol.io/specification/2025-11-25/server/utilities/logging

This document describes how to implement logging in MCP servers and how clients can consume log messages.

### Logging Levels

MCP uses the logging levels defined in [RFC 5424](https://tools.ietf.org/html/rfc5424).

The MCP C# SDK uses the standard .NET [ILogger] and [ILoggerProvider] abstractions, which support a slightly
different set of logging levels. The following table shows the levels and how they map to standard .NET logging levels.

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

[Initialization]: https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle#initialization

Servers built with the C# SDK always declare the logging capability. Doing so does not obligate the server
to send log messages&mdash;only allows it. Note that stateless MCP servers might not be capable of sending log
messages as there might not be an open connection to the client on which the log messages could be sent.

The C# SDK provides an extension method <xref:Microsoft.Extensions.DependencyInjection.McpServerBuilderExtensions.WithSetLoggingLevelHandler*> on <xref:Microsoft.Extensions.DependencyInjection.IMcpServerBuilder> to allow the
server to perform any special logic it wants to perform when a client sets the logging level. However, the
SDK already takes care of setting the <xref:ModelContextProtocol.Server.McpServer.LoggingLevel> in the <xref:ModelContextProtocol.Server.McpServer>, so most servers will not need to
implement this.

MCP Servers using the MCP C# SDK can obtain an [ILoggerProvider](https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.iloggerprovider) from the <xref:ModelContextProtocol.Server.McpServer.AsClientLoggerProvider> method on <xref:ModelContextProtocol.Server.McpServer>,
and from that can create an [ILogger](https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.ilogger) instance for logging messages that should be sent to the MCP client.

[!code-csharp[](samples/server/Tools/LoggingTools.cs?name=snippet_LoggingConfiguration)]

### Client support for logging

When the server indicates that it supports logging, clients should configure
the logging level to specify which messages the server should send to the client.

Clients should check if the server supports logging by checking the <xref:ModelContextProtocol.Protocol.ServerCapabilities.Logging> property of the <xref:ModelContextProtocol.Client.McpClient.ServerCapabilities> field of <xref:ModelContextProtocol.Client.McpClient>.

[!code-csharp[](samples/client/Program.cs?name=snippet_LoggingCapabilities)]

If the server supports logging, the client should set the level of log messages it wishes to receive with
the <xref:ModelContextProtocol.Client.McpClient.SetLoggingLevelAsync*> method on <xref:ModelContextProtocol.Client.McpClient>. If the client does not set a logging level, the server might choose
to send all log messages or none&mdash;this is not specified in the protocol. So it's important that the client
sets a logging level to ensure it receives the desired log messages and only those messages.

The `loggingLevel` set by the client is an MCP logging level.
See the [Logging Levels](#logging-levels) section above for the mapping between MCP and .NET logging levels.

[!code-csharp[](samples/client/Program.cs?name=snippet_LoggingLevel)]

Lastly, the client must configure a notification handler for <xref:ModelContextProtocol.Protocol.NotificationMethods.LoggingMessageNotification> notifications.
The following example simply writes the log messages to the console.

[!code-csharp[](samples/client/Program.cs?name=snippet_LoggingHandler)]
