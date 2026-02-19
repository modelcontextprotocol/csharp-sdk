# ASP.NET Core extensions for the MCP C# SDK

[![NuGet preview version](https://img.shields.io/nuget/vpre/ModelContextProtocol.svg)](https://www.nuget.org/packages/ModelContextProtocol/absoluteLatest)

The official C# SDK for the [Model Context Protocol](https://modelcontextprotocol.io/), enabling .NET applications, services, and libraries to implement and interact with MCP clients and servers. Please visit our [API documentation](https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.html) for more details on available functionality.

> [!NOTE]
> This project is in preview; breaking changes can be introduced without prior notice.

## About MCP

The Model Context Protocol (MCP) is an open protocol that standardizes how applications provide context to Large Language Models (LLMs). It enables secure integration between LLMs and various data sources and tools.

For more information about MCP:

- [Official Documentation](https://modelcontextprotocol.io/)
- [Protocol Specification](https://modelcontextprotocol.io/specification/)
- [GitHub Organization](https://github.com/modelcontextprotocol)

## Installation

To get started, install the package from NuGet

```
dotnet new web
dotnet add package ModelContextProtocol.AspNetCore --prerelease
```

## Getting Started

```csharp
// Program.cs
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

## Using with MVC Controllers

If your application uses traditional MVC controllers instead of minimal APIs,
you can use `McpRequestDelegateFactory.Create()` to create a `RequestDelegate` that handles MCP requests:

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();
var app = builder.Build();

app.MapControllers(); // No MapMcp() needed!

app.Run("http://localhost:3001");
```

```csharp
// Controllers/McpController.cs
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ModelContextProtocol.AspNetCore;

[ApiController]
[Route("mcp")]
public class McpController : ControllerBase
{
    private static readonly RequestDelegate _mcpHandler = McpRequestDelegateFactory.Create();

    [HttpPost]
    [HttpGet]
    [HttpDelete]
    public Task Handle() => _mcpHandler(HttpContext);
}
```
