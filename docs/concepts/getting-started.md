---
title: Getting Started
author: jeffhandley
description: Install the MCP C# SDK and build your first MCP client and server.
uid: getting-started
---

## Getting Started

This guide walks you through installing the MCP C# SDK and building a minimal MCP client and server.

### Choosing a package

The SDK ships as three NuGet packages. Pick the one that matches your scenario:

| Package | Use when... |
| - | - |
| **[ModelContextProtocol.Core](https://www.nuget.org/packages/ModelContextProtocol.Core/absoluteLatest)** | You only need the client or low-level server APIs and want the **minimum set of dependencies**. |
| **[ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol/absoluteLatest)** | You're building a client or a **stdio-based** server and want hosting, dependency injection, and attribute-based tool/prompt/resource discovery. References `ModelContextProtocol.Core`. **This is the right starting point for most projects.** |
| **[ModelContextProtocol.AspNetCore](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore/absoluteLatest)** | You're building an **HTTP-based** MCP server hosted in ASP.NET Core. References `ModelContextProtocol`, so you get everything above plus the HTTP transport. |

> [!TIP]
> If you're unsure, start with the **ModelContextProtocol** package. You can always add **ModelContextProtocol.AspNetCore** later if you need HTTP transport.

### Building an MCP server

> [!TIP]
> You can also use the [MCP Server project template](https://learn.microsoft.com/dotnet/ai/quickstarts/build-mcp-server) to quickly scaffold a new MCP server project.

Create a new console app, add the required packages, and replace `Program.cs` with the code below to get a working MCP server that exposes a single tool over stdio:

```
dotnet new console
dotnet add package ModelContextProtocol
dotnet add package Microsoft.Extensions.Hosting
```

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(consoleLogOptions =>
{
    // Configure all logs to go to stderr
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();

[McpServerToolType]
public static class EchoTool
{
    [McpServerTool, Description("Echoes the message back to the client.")]
    public static string Echo(string message) => $"hello {message}";
}
```

The call to `WithToolsFromAssembly` discovers every class marked with `[McpServerToolType]` in the assembly and registers every `[McpServerTool]` method as a tool. Prompts and resources work the same way with `[McpServerPromptType]` / `[McpServerPrompt]` and `[McpServerResourceType]` / `[McpServerResource]`.

For HTTP-based servers using ASP.NET Core:

```
dotnet new web
dotnet add package ModelContextProtocol.AspNetCore
```

```csharp
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();
var app = builder.Build();

app.MapMcp();

app.Run("http://localhost:3001");

[McpServerToolType]
public static class EchoTool
{
    [McpServerTool, Description("Echoes the message back to the client.")]
    public static string Echo(string message) => $"hello {message}";
}
```

### Building an MCP client

Create a new console app, add the package, and replace `Program.cs` with the code below. This client connects to the [MCP "everything" reference server](https://www.npmjs.com/package/@modelcontextprotocol/server-everything), lists its tools, and calls one:

```
dotnet new console
dotnet add package ModelContextProtocol
```

```csharp
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "Everything",
    Command = "npx",
    Arguments = ["-y", "@modelcontextprotocol/server-everything"],
});

var client = await McpClient.CreateAsync(clientTransport);

// Print the list of tools available from the server.
foreach (var tool in await client.ListToolsAsync())
{
    Console.WriteLine($"{tool.Name} ({tool.Description})");
}

// Execute a tool (this would normally be driven by LLM tool invocations).
var result = await client.CallToolAsync(
    "echo",
    new Dictionary<string, object?>() { ["message"] = "Hello MCP!" },
    cancellationToken: CancellationToken.None);

// echo always returns one and only one text content object
Console.WriteLine(result.Content.OfType<TextContentBlock>().First().Text);
```

Clients can connect to any MCP server, not just ones created with this library. The protocol is server-agnostic.

#### Using tools with an LLM

`McpClientTool` inherits from `AIFunction`, so the tools returned by `ListToolsAsync` can be handed directly to any `IChatClient`:

```csharp
// Get available tools.
IList<McpClientTool> tools = await client.ListToolsAsync();

// Call the chat client using the tools.
IChatClient chatClient = ...;
var response = await chatClient.GetResponseAsync(
    "your prompt here",
    new() { Tools = [.. tools] });
```

### Next steps

Explore the rest of the conceptual documentation to learn about [tools](tools/tools.md), [prompts](prompts/prompts.md), [resources](resources/resources.md), [transports](transports/transports.md), and more. You can also browse the [samples](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples) directory for complete end-to-end examples.
