---
title: Logging
author: mikekistler
description: How to use the logging feature in the MCP C# SDK.
uid: logging
---

## Logging

MCP servers can expose log messages to clients through the [Logging utility].

[Logging utility]: https://modelcontextprotocol.io/specification/2025-11-25/server/utilities/logging

> [!IMPORTANT]
> Logging is **deprecated** as of MCP specification revision `2026-07-28` ([SEP-2577](https://github.com/modelcontextprotocol/modelcontextprotocol/pull/2577), [MCP9005](xref:list-of-diagnostics#obsolete-apis)) and may be removed in a future version.

This document describes how to implement logging in MCP servers and how clients can consume log messages.

### Logging levels

MCP uses the logging levels defined in [RFC 5424](https://datatracker.ietf.org/doc/html/rfc5424).

The MCP C# SDK uses the standard .NET [ILogger] and [ILoggerProvider] abstractions, which support a slightly
different set of logging levels. The following table shows the levels and how they map to standard .NET logging levels.

| Level       | .NET | Description                      | Example use case           |
|-------------|------|----------------------------------|----------------------------|
| `debug`     | ✓   | Detailed debugging information   | Function entry/exit points |
| `info`      | ✓   | General informational messages   | Operation progress updates |
| `notice`    |      | Normal but significant events    | Configuration changes      |
| `warning`   | ✓   | Warning conditions               | Deprecated feature usage   |
| `error`     | ✓   | Error conditions                 | Operation failures         |
| `critical`  | ✓   | Critical conditions              | System component failures  |
| `alert`     |      | Action must be taken immediately | Data corruption detected   |
| `emergency` |      | System is unusable               |                            |

**Note:** .NET's [ILogger] also supports a `Trace` level (more verbose than Debug) log level.
As there is no more verbose level in the MCP logging levels, Trace level log messages are mapped to
the MCP `debug` level when sent to the client.

[ILogger]: https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.ilogger
[ILoggerProvider]: https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.iloggerprovider

### Server configuration and logging

MCP servers that implement the Logging utility must declare this in the capabilities sent in the
[Initialization] phase at the beginning of the MCP session.

[Initialization]: https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle#initialization

Servers built with the C# SDK always declare the logging capability. Doing so does not obligate the server
to send log messages&mdash;only allows it. Note that [stateless](xref:stateless) MCP servers might not be capable of sending log
messages as there might not be an open connection to the client on which the log messages could be sent.

The C# SDK provides an extension method <xref:Microsoft.Extensions.DependencyInjection.McpServerBuilderExtensions.WithSetLoggingLevelHandler*> on <xref:Microsoft.Extensions.DependencyInjection.IMcpServerBuilder> to allow the
server to perform any special logic it wants to perform when a client sets the logging level. However, the
SDK already takes care of setting the <xref:ModelContextProtocol.Server.McpServer.LoggingLevel> in the <xref:ModelContextProtocol.Server.McpServer>, so most servers don't need to
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
For the mapping between MCP and .NET logging levels, see the [Logging Levels](#logging-levels) section.

[!code-csharp[](samples/client/Program.cs?name=snippet_LoggingLevel)]

Lastly, the client must configure a notification handler for <xref:ModelContextProtocol.Protocol.NotificationMethods.LoggingMessageNotification> notifications.
The following example simply writes the log messages to the console.

[!code-csharp[](samples/client/Program.cs?name=snippet_LoggingHandler)]

### Sanitizing exceptions in the server's own diagnostic logs

Separately from the MCP Logging utility described above, the server writes its own diagnostic logs to the
[ILogger] it was configured with. When a request handler, tool, prompt, or resource throws, those logs include the
raw <xref:System.Exception>, which most logging providers render as the exception message plus its stack trace.
That output can contain sensitive or overly detailed runtime data.

Set <xref:ModelContextProtocol.Server.McpServerOptions.ExceptionSummarizer> to log a sanitized description instead.
When it is set, the failure paths log only the string the delegate returns, and the raw exception is not attached to
the log entry. The default is `null`, which preserves the existing behavior of logging the raw exception.

```csharp
builder.Services.AddMcpServer(options =>
{
    options.ExceptionSummarizer = ex => ex.GetType().Name;
});
```

The summarized and raw forms of each event share one `EventId`, so filters and alerts keyed on `EventId` behave the
same whether or not a summarizer is configured. The delegate runs only when the corresponding log level is enabled,
so it costs nothing on a level that is filtered out.

The `ModelContextProtocol` package also integrates with the standard
[Microsoft.Extensions.Diagnostics.ExceptionSummarization](https://learn.microsoft.com/dotnet/api/microsoft.extensions.diagnostics.exceptionsummarization)
abstractions. If an `IExceptionSummarizer` is registered in the container and `ExceptionSummarizer` has not been set
explicitly, the SDK populates it with `"{ExceptionType}: {Description}"` taken from the `ExceptionSummary`:

```csharp
builder.Services.AddExceptionSummarizer(b => b.AddHttpProvider());
builder.Services.AddMcpServer();
```

Both of those fields are documented as free of privacy-sensitive information. `ExceptionSummary.AdditionalDetails` is
not, and `ExceptionSummary.ToString()` appends it, so neither is used. The exception type is included because
`Description` on its own is `"Unknown"` for exception types that no registered provider handles.

If the delegate throws or returns `null`, the SDK falls back to logging the raw exception, so a faulty summarizer
can never fail the session.
