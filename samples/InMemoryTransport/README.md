# InMemoryTransport Sample

This sample demonstrates how to create an MCP client and server connected via an in-memory pipe, without using network sockets or stdio. This is useful for testing and embedding MCP servers directly in your application.

## Requirements

- .NET 8.0 SDK or later
- No project file required!

## Running the Sample

Simply run the Program.cs file directly:

```bash
dotnet run Program.cs
```

Or on Unix-like systems, make the file executable:

```bash
chmod +x Program.cs
./Program.cs
```

## What This Sample Shows

- Creating a server with `StreamServerTransport` over an in-memory pipe
- Connecting a client using `StreamClientTransport` over the same pipe
- Listing available tools from the server
- Invoking tools on the server

The sample creates a simple "Echo" tool that echoes back the input message.

## Reference

- [File-Based Programs Tutorial](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/tutorials/file-based-programs)
- [Model Context Protocol Specification](https://modelcontextprotocol.io/specification/)
