# List of Diagnostics Produced by MCP C# SDK

This document provides information about each of the diagnostics produced by the MCP C# SDK analyzers and source generators.

## Analyzer Diagnostics

Analyzer diagnostic IDs are in the format `MCP###`.

| Diagnostic ID | Description |
| :------------ | :---------- |
| `MCP001` | Invalid XML documentation for MCP method |
| `MCP002` | MCP method must be partial to generate [Description] attributes |

## Experimental APIs

Experimental diagnostic IDs are in the format `MCPEXP###`.

As new functionality is introduced to this SDK, new in-development APIs are marked as being experimental. Experimental APIs offer no compatibility guarantees and can change without notice. They are usually published in order to gather feedback before finalizing a design.

You may use experimental APIs in your application, but we advise against using these APIs in production scenarios as they may not be fully tested nor fully reliable. Additionally, we strongly recommend that library authors do not publish versions of their libraries that depend on experimental APIs as this will quite possibly lead to future breaking changes and diamond problems.

If you use experimental APIs, you will get one of the diagnostics shown below. The diagnostic is there to let you know you're using such an API so that you can avoid accidentally depending on experimental features. You may suppress these diagnostics if desired.

| Diagnostic ID | Description |
| :------------ | :---------- |
| `MCPEXP001` | MCP experimental APIs including Tasks and Extensions. Tasks provide a mechanism for asynchronous long-running operations that can be polled for status and results (see [MCP Tasks specification](https://modelcontextprotocol.io/specification/draft/basic/utilities/tasks)). Extensions provide a framework for extending the Model Context Protocol while maintaining interoperability (see [SEP-2133](https://github.com/modelcontextprotocol/modelcontextprotocol/pull/2133)). |
| `MCPEXP002` | Subclassing `McpClient` and `McpServer` is experimental and subject to change. |