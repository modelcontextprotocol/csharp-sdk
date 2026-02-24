# MCP C# SDK Core

[![NuGet preview version](https://img.shields.io/nuget/vpre/ModelContextProtocol.Core.svg)](https://www.nuget.org/packages/ModelContextProtocol.Core/absoluteLatest)

Core .NET SDK for the [Model Context Protocol](https://modelcontextprotocol.io/), enabling .NET applications, services, and libraries to implement and interact with MCP clients and servers. Please visit our [API documentation](https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.html) for more details on available functionality.

> [!NOTE]
> This project is in preview; breaking changes can be introduced without prior notice.

## About MCP

The Model Context Protocol (MCP) is an open protocol that standardizes how applications provide context to Large Language Models (LLMs). It enables secure integration between LLMs and various data sources and tools.

For more information about MCP:

- [Official Documentation](https://modelcontextprotocol.io/)
- [Protocol Specification](https://modelcontextprotocol.io/specification/)
- [GitHub Organization](https://github.com/modelcontextprotocol)

## Installation

To get started, install the core package from NuGet

```
dotnet add package ModelContextProtocol.Core --prerelease
```

## Getting Started (Client)

To get started writing a client, the `McpClient.CreateAsync` method is used to instantiate and connect an `McpClient`
to a server. Once you have an `McpClient`, you can interact with it, such as to enumerate all available tools and invoke tools.

```csharp
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

Clients can connect to any MCP server, not just ones created using this library. The protocol is designed to be server-agnostic, so you can use this library to connect to any compliant server.

Tools can be easily exposed for immediate use by `IChatClient`s, because `McpClientTool` inherits from `AIFunction`.

```csharp
// Get available functions.
IList<McpClientTool> tools = await client.ListToolsAsync();

// Call the chat client using the tools.
IChatClient chatClient = ...;
var response = await chatClient.GetResponseAsync(
    "your prompt here",
    new() { Tools = [.. tools] });
```

## Getting Started (Server)

The core package provides the basic server functionality. Here's an example of creating a simple MCP server without dependency injection:

```csharp
using ModelContextProtocol.Server;
using System.ComponentModel;

// Create server options with tools
var serverOptions = new McpServerOptions
{
    ToolCollection = [
        McpServerTool.Create((string message) => $"hello {message}", new()
        {
            Name = "echo",
            Description = "Echoes the message back to the client."
        })
    ]
};

// Create and run server with stdio transport
await using var stdioTransport = new StdioServerTransport("EchoServer");
await using var server = McpServer.Create(stdioTransport, serverOptions);
await server.RunAsync(CancellationToken.None);
```

For more advanced scenarios with dependency injection, hosting, and automatic tool discovery, see the `ModelContextProtocol` package.
