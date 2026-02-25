# MCP C# SDK

[![NuGet version](https://img.shields.io/nuget/v/ModelContextProtocol.svg)](https://www.nuget.org/packages/ModelContextProtocol)

The official C# SDK for the [Model Context Protocol](https://modelcontextprotocol.io/), enabling .NET applications, services, and libraries to implement and interact with MCP clients and servers. Please visit the [API documentation](https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.html) for more details on available functionality.

## Packages

This SDK consists of three main packages:

- **[ModelContextProtocol.Core](https://www.nuget.org/packages/ModelContextProtocol.Core)** [![NuGet version](https://img.shields.io/nuget/v/ModelContextProtocol.Core.svg)](https://www.nuget.org/packages/ModelContextProtocol.Core) - For projects that only need to use the client or low-level server APIs and want the minimum number of dependencies. [Documentation](https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.html)

- **[ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol)** [![NuGet version](https://img.shields.io/nuget/v/ModelContextProtocol.svg)](https://www.nuget.org/packages/ModelContextProtocol) - The main package with hosting and dependency injection extensions. References `ModelContextProtocol.Core`. This is the right fit for most projects that don't need HTTP server capabilities. [Documentation](https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.html)

- **[ModelContextProtocol.AspNetCore](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore)** [![NuGet version](https://img.shields.io/nuget/v/ModelContextProtocol.AspNetCore.svg)](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore) - The library for HTTP-based MCP servers. References `ModelContextProtocol`. [Documentation](https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.AspNetCore.html)

## Getting Started

To get started, see the [Getting Started](https://modelcontextprotocol.github.io/csharp-sdk/concepts/getting-started.html) guide in the conceptual documentation for installation instructions, package-selection guidance, and complete examples for both clients and servers.

You can also browse the [samples](samples) directory and the [API documentation](https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.html) for more details on available functionality.

## About MCP

The Model Context Protocol (MCP) is an open protocol that standardizes how applications provide context to Large Language Models (LLMs). It enables secure integration between LLMs and various data sources and tools.

For more information about MCP:

- [Official MCP Documentation](https://modelcontextprotocol.io/)
- [MCP C# SDK Documentation](https://modelcontextprotocol.github.io/csharp-sdk/)
- [Protocol Specification](https://modelcontextprotocol.io/specification/)
- [GitHub Organization](https://github.com/modelcontextprotocol)

## License

This project is licensed under the [Apache License 2.0](LICENSE).
