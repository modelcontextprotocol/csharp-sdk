# EverythingServer.Stdio

Stdio-based MCP server demonstrating all MCP capabilities using standard input/output communication.

## Overview

This is the stdio transport implementation of the EverythingServer sample. It demonstrates how to build an MCP server that communicates via standard input/output (stdio), which is the most common way to integrate MCP servers with desktop applications and AI assistants.

## Features

The stdio server includes all the same capabilities as the HTTP version:

- Tools (add, echo, long-running operations, etc.)
- Prompts (simple and complex with arguments)
- Resources with subscriptions
- Sampling (LLM integration)
- Logging level management
- Progress reporting
- OpenTelemetry integration

## Running the Server

```bash
dotnet run --project samples/EverythingServer.Stdio/EverythingServer.Stdio.csproj
```

The server will read JSON-RPC messages from stdin and write responses to stdout. All diagnostic logging is sent to stderr to avoid interfering with the MCP protocol.

## Using with MCP Clients

To use this server with an MCP client, configure it to launch the executable:

```json
{
  "mcpServers": {
    "everything-server": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "path/to/samples/EverythingServer.Stdio/EverythingServer.Stdio.csproj"
      ]
    }
  }
}
```

Or if you've published the application:

```json
{
  "mcpServers": {
    "everything-server": {
      "command": "path/to/EverythingServer.Stdio"
    }
  }
}
```

## Architecture

This project uses:
- `EverythingServer.Core` for shared MCP handlers
- Microsoft.Extensions.Hosting for the host builder
- `WithStdioServerTransport()` for stdio communication
- Hosted background services for subscriptions and logging updates
- Console logging configured to write to stderr

## Key Differences from HTTP Version

- **Single Session**: Stdio servers typically handle one session per process
- **Hosted Services**: Background services are registered as hosted services instead of being started per-session
- **Logging**: All logs go to stderr to keep stdout clean for MCP protocol messages
