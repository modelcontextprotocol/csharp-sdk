# File-Based MCP Server Sample

This sample demonstrates how to create a complete MCP (Model Context Protocol) server using .NET 10's file-based programs feature. Unlike traditional .NET projects that require a `.csproj` file, file-based programs allow you to write and run complete applications in a single `.cs` file.

## Requirements

- .NET 10 SDK (RC2 or later)
- No project file required!

## Running the Sample

Simply run the Program.cs file directly:

```bash
dotnet run Program.cs
```

The server will start and listen for MCP messages on stdin/stdout (stdio transport).

### Making it Executable (Unix/Linux/macOS)

On Unix-like systems, you can make the file executable:

```bash
chmod +x Program.cs
./Program.cs
```

Note: The shebang line uses `/usr/bin/env` to locate `dotnet`, so ensure it's in your PATH.

## Testing the Server

You can test the server by using `@modelcontextprotocol/inspector`, any stdio-compatible client, or sending JSON-RPC messages to stdin. Here's an example:

### Initialize the server:
```bash
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test-client","version":"1.0"}}}' | dotnet run Program.cs
```

### List available tools:
```bash
(
  echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test-client","version":"1.0"}}}'
  sleep 0.5
  echo '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
  sleep 1
) | dotnet run Program.cs 2>/dev/null | grep '^{' | jq .
```

### Call the echo tool:
```bash
(
  echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test-client","version":"1.0"}}}'
  sleep 0.5
  echo '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"echo","arguments":{"message":"Hello, MCP!"}}}'
  sleep 1
) | dotnet run Program.cs 2>/dev/null | grep '^{' | jq .
```

## Reference

- [File-Based Programs Tutorial](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/tutorials/file-based-programs)
- [C# Preprocessor Directives for File-Based Apps](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/preprocessor-directives#file-based-apps)
- [Model Context Protocol Specification](https://spec.modelcontextprotocol.io/)
